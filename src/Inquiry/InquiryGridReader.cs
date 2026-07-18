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
        if (!_hasResultSet)
        {
            throw new InvalidOperationException(
                "The Inquiry grid reader has no more result sets. Read each result set exactly once, in order.");
        }
    }

    private async Task AdvanceAsync(CancellationToken cancellationToken)
        => _hasResultSet = await _reader.NextResultAsync(cancellationToken).ConfigureAwait(false);

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
                        new KeyValuePair<string, object?>("db.operation.name", "BATCH"));
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
            new KeyValuePair<string, object?>("db.operation.name", "BATCH"),
            new KeyValuePair<string, object?>("error.type", errorType));
        if (_activity is { } activity)
        {
            activity.SetTag("error.type", errorType);
            activity.SetStatus(ActivityStatusCode.Error, exception.Message);
        }
    }
}
