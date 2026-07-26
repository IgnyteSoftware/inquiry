using Inquiry.Commands;
using Inquiry.Diagnostics;
using Inquiry.Materialization;
using Inquiry.Pipeline;
using System.Data.Common;
using System.Diagnostics;

namespace Inquiry;

/// <summary>
/// Reads multiple result sets produced by a single executed command (one round trip), materializing each
/// set in order with a struct materializer. Returned by
/// <see cref="IInquiry.QueryMultipleAsync(Inquiry.Commands.InquiryCommand, System.Threading.CancellationToken)"/>
/// and consumed by generated eager-load stores to fetch a parent entity and its key-filterable child
/// collections in a single round trip — the equivalent of Dapper's <c>QueryMultiple</c> / a multi-result
/// stored procedure. Dispose to release the reader, command, and (when the grid owns it) the connection.
/// </summary>
/// <remarks>
/// Result sets must be read exactly once, in declaration order, with the matching entity + materializer.
/// Reads use <see cref="System.Data.CommandBehavior.SequentialAccess"/> (generated materializers read each
/// column once in ascending ordinal order), so a forward-only read of each set is safe. When telemetry is
/// enabled, the grid span covers the entire reader lifetime — from command execution to disposal.
/// </remarks>
public sealed class InquiryGridReader : IAsyncDisposable
{
    private readonly DbDataReader _reader;
    private readonly DbCommand _command;
    private readonly DbConnection? _ownedConnection;
    private readonly IDisposable? _lease;
    private readonly Activity? _activity;
    private readonly long _startTimestamp;
    private bool _hasResultSet;
    private bool _disposed;
    private bool _faulted;
    private bool _streaming;

    internal InquiryGridReader(DbDataReader reader, DbCommand command, DbConnection? ownedConnection, IDisposable? lease,
        Activity? activity = null, long startTimestamp = 0)
    {
        _reader = reader;
        _command = command;
        _ownedConnection = ownedConnection;
        _lease = lease;
        _activity = activity;
        _startTimestamp = startTimestamp;
        _hasResultSet = true;
    }

    /// <summary>
    /// Materializes the first row of the current result set (or <see langword="null"/> when empty), then
    /// advances to the next result set.
    /// </summary>
    public async Task<TEntity?> ReadSingleOrDefaultAsync<TEntity, TMaterializer>(
        TMaterializer materializer,
        CancellationToken cancellationToken = default)
        where TEntity : class
        where TMaterializer : struct, IInquiryEntityMaterializer<TEntity>
    {
        EnsureResultSet();
        try
        {
            TEntity? result = null;
            if (await _reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                result = materializer.Materialize(_reader);
                // Match QuerySingleOrDefaultAsync: a "single" read rejects a second row rather than silently
                // truncating the set. SequentialAccess only forbids re-reading columns, not advancing rows, so
                // the materializer has already consumed row 1's columns and this Read just probes for a second.
                if (await _reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    throw new InvalidOperationException(
                        "ReadSingleOrDefaultAsync expected zero or one row, but the result set returned multiple rows.");
                }
            }

            await AdvanceAsync(cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch (Exception exception)
        {
            RecordFailure(exception);
            throw;
        }
    }

    /// <summary>
    /// Reads a generator-proven single-row result set without issuing a duplicate probe, then
    /// advances to the next result set.
    /// </summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public async Task<TEntity?> ReadGeneratedSingleOrDefaultAsync<TEntity, TMaterializer>(
        TMaterializer materializer,
        CancellationToken cancellationToken = default)
        where TEntity : class
        where TMaterializer : struct, IInquiryEntityMaterializer<TEntity>
    {
        EnsureResultSet();
        try
        {
            TEntity? result = null;
            if (await _reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                result = materializer.Materialize(_reader);
            }

            await AdvanceAsync(cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch (Exception exception)
        {
            RecordFailure(exception);
            throw;
        }
    }

    /// <summary>
    /// Materializes each row of the current result set and passes it to <paramref name="action"/> without
    /// buffering an intermediate list, then advances to the next result set. Used by generated eager-load
    /// stores to build grouping dictionaries directly from the reader.
    /// </summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public async Task ReadForEachAsync<TEntity, TMaterializer>(
        TMaterializer materializer,
        Action<TEntity> action,
        CancellationToken cancellationToken = default)
        where TEntity : class
        where TMaterializer : struct, IInquiryEntityMaterializer<TEntity>
    {
        EnsureResultSet();
        try
        {
            while (await _reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                action(materializer.Materialize(_reader));
            }

            await AdvanceAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            RecordFailure(exception);
            throw;
        }
    }

    /// <summary>
    /// Streams each row of the current result set as it is materialized — no intermediate list — then
    /// advances to the next result set once enumeration completes. The pull-based counterpart to
    /// <see cref="ReadForEachAsync{TEntity, TMaterializer}"/>, used by generated eager-load stores to
    /// yield parent entities straight out of the grid's last result set.
    /// </summary>
    /// <remarks>
    /// Enumerate the returned sequence exactly once and to completion before reading any further result
    /// set. Abandoning it early (a <c>break</c>, or <c>Take(n)</c>) leaves the reader positioned mid-set;
    /// the grid then refuses every subsequent read and may only be disposed. Without that guard the next
    /// read would silently return the tail of this set as though it were the following one.
    /// Enumerating the sequence a second time throws for the same reason — it would read the grid's
    /// <em>next</em> result set through this materializer.
    /// </remarks>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public IAsyncEnumerable<TEntity> ReadStreamAsync<TEntity, TMaterializer>(
        TMaterializer materializer,
        CancellationToken cancellationToken = default)
        where TEntity : class
        where TMaterializer : struct, IInquiryEntityMaterializer<TEntity>
    {
        // Validate eagerly, like every sibling read: an iterator body would not run until the first
        // MoveNextAsync, so a disposed grid or an exhausted result set would surface late.
        EnsureResultSet();
        return ReadStreamCore<TEntity, TMaterializer>(materializer, new StreamGuard(), cancellationToken);
    }

    private async IAsyncEnumerable<TEntity> ReadStreamCore<TEntity, TMaterializer>(
        TMaterializer materializer,
        StreamGuard guard,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        where TEntity : class
        where TMaterializer : struct, IInquiryEntityMaterializer<TEntity>
    {
        // A second GetAsyncEnumerator on the returned sequence clones the compiler-generated state
        // machine and re-runs this body against an already-advanced reader — which would materialize the
        // FOLLOWING result set through this materializer and then skip past it, silently. The guard makes
        // the sequence single-use; it also rejects two enumerators running concurrently over one reader.
        guard.Enter();

        // Re-validate: the eager check ran when the sequence was created, and the grid may have been
        // disposed between then and the first MoveNextAsync.
        EnsureResultSet();
        _streaming = true;

        // yield return cannot sit inside a try that has a catch, so each await is guarded separately —
        // the same shape InquiryRequestPipeline.QueryAsync uses. RecordFailure is set-once, so the
        // duplicated catch cannot double-record.
        while (true)
        {
            bool hasRow;
            try
            {
                hasRow = await _reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                RecordFailure(exception);
                throw;
            }

            if (!hasRow) break;

            TEntity item;
            try
            {
                item = materializer.Materialize(_reader);
            }
            catch (Exception exception)
            {
                RecordFailure(exception);
                throw;
            }

            yield return item;
        }

        try
        {
            await AdvanceAsync(cancellationToken).ConfigureAwait(false);
            _streaming = false;
        }
        catch (Exception exception)
        {
            RecordFailure(exception);
            throw;
        }
    }

    /// <summary>
    /// Materializes the current result set into a list, then advances to the next result set.
    /// </summary>
    public async Task<List<TEntity>> ReadListAsync<TEntity, TMaterializer>(
        TMaterializer materializer,
        CancellationToken cancellationToken = default)
        where TEntity : class
        where TMaterializer : struct, IInquiryEntityMaterializer<TEntity>
    {
        EnsureResultSet();
        try
        {
            var list = new List<TEntity>();
            while (await _reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                list.Add(materializer.Materialize(_reader));
            }

            await AdvanceAsync(cancellationToken).ConfigureAwait(false);
            return list;
        }
        catch (Exception exception)
        {
            RecordFailure(exception);
            throw;
        }
    }

    /// <summary>
    /// Reads the first column of the first row of the current result set as <typeparamref name="T"/>
    /// (or <c>default(T)</c> when empty or null), then advances to the next result set.
    /// </summary>
    public async Task<T> ReadScalarAsync<T>(CancellationToken cancellationToken = default)
    {
        EnsureResultSet();
        try
        {
            var result = default(T);
            if (await _reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                result = ScalarConvert.From<T>(_reader.GetValue(0));
            }

            await AdvanceAsync(cancellationToken).ConfigureAwait(false);
            return result!;
        }
        catch (Exception exception)
        {
            RecordFailure(exception);
            throw;
        }
    }

    private void EnsureResultSet()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_streaming)
        {
            // Either a streaming read is still being enumerated, or it was abandoned before completing.
            // Both leave the reader mid-set, so any further read would silently return this set's tail.
            throw new InvalidOperationException(
                "A streaming read of the current Inquiry grid result set is still in progress or was " +
                "abandoned before completion. Enumerate ReadStreamAsync exactly once, to completion, " +
                "before reading another result set.");
        }

        if (!_hasResultSet)
        {
            throw new InvalidOperationException(
                "The Inquiry grid reader has no more result sets. Read each result set exactly once, in order.");
        }
    }

    private async Task AdvanceAsync(CancellationToken cancellationToken)
        => _hasResultSet = await _reader.NextResultAsync(cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// One-shot gate for a streaming read. Async iterators hand out a fresh state machine on every
    /// <c>GetAsyncEnumerator</c>, so the sequence itself carries no "already consumed" state — this does.
    /// </summary>
    private sealed class StreamGuard
    {
        private int _entered;

        internal void Enter()
        {
            if (Interlocked.Exchange(ref _entered, 1) != 0)
            {
                throw new InvalidOperationException(
                    "The sequence returned by ReadStreamAsync can only be enumerated once. Enumerating it " +
                    "again would read the grid's next result set through this materializer.");
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        List<Exception>? exceptions = null;
        try
        {
            if (_startTimestamp != 0)
            {
                var dbSystem = InquiryTelemetry.MapDbSystem(_command);
                if (!_faulted)
                {
                    InquiryTelemetry.CommandDuration.Record(
                        Stopwatch.GetElapsedTime(_startTimestamp).TotalSeconds,
                        new KeyValuePair<string, object?>("db.system.name", dbSystem),
                        new KeyValuePair<string, object?>("db.operation.name", "QUERY_MULTIPLE"));
                }

                _activity?.Dispose();
            }
        }
        catch (Exception exception) { exceptions = InquiryCleanup.Add(exceptions, exception); }
        try
        {
            await _reader.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            exceptions = InquiryCleanup.Add(exceptions, exception);
        }
        try { InquiryCommandResources.Dispose(_command); }
        catch (Exception exception) { exceptions = InquiryCleanup.Add(exceptions, exception); }
        try { await _command.DisposeAsync().ConfigureAwait(false); }
        catch (Exception exception) { exceptions = InquiryCleanup.Add(exceptions, exception); }
        try
        {
            if (_ownedConnection is not null) await _ownedConnection.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception) { exceptions = InquiryCleanup.Add(exceptions, exception); }
        try { _lease?.Dispose(); }
        catch (Exception exception) { exceptions = InquiryCleanup.Add(exceptions, exception); }
        InquiryCleanup.ThrowIfAny(exceptions);
    }

    internal void RecordFailure(Exception exception)
    {
        if (_startTimestamp == 0 || _faulted) return;
        _faulted = true;
        var errorType = exception.GetType().FullName ?? exception.GetType().Name;
        var dbSystem = InquiryTelemetry.MapDbSystem(_command);
        InquiryTelemetry.CommandDuration.Record(
            Stopwatch.GetElapsedTime(_startTimestamp).TotalSeconds,
            new KeyValuePair<string, object?>("db.system.name", dbSystem),
            new KeyValuePair<string, object?>("db.operation.name", "QUERY_MULTIPLE"),
            new KeyValuePair<string, object?>("error.type", errorType));
        if (_activity is { } activity)
        {
            activity.SetTag("error.type", errorType);
            activity.SetStatus(ActivityStatusCode.Error, exception.Message);
        }
    }
}
