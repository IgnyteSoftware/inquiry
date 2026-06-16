using Inquiry.Materialization;
using System.Data.Common;

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
/// column once in ascending ordinal order), so a forward-only read of each set is safe. This path bypasses
/// command interceptors / telemetry — its lifetime spans multiple reads, so there is no single
/// command-executed moment to surface (the same trade-off the bulk-insert path makes).
/// </remarks>
public sealed class InquiryGridReader : IAsyncDisposable
{
    private readonly DbDataReader _reader;
    private readonly DbCommand _command;
    private readonly DbConnection? _ownedConnection;
    private readonly IDisposable? _lease;
    private bool _hasResultSet;
    private bool _disposed;

    internal InquiryGridReader(DbDataReader reader, DbCommand command, DbConnection? ownedConnection, IDisposable? lease)
    {
        _reader = reader;
        _command = command;
        _ownedConnection = ownedConnection;
        _lease = lease;
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
        TEntity? result = null;
        if (await _reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result = materializer.Materialize(_reader);
        }

        await AdvanceAsync(cancellationToken).ConfigureAwait(false);
        return result;
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
        var list = new List<TEntity>();
        while (await _reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(materializer.Materialize(_reader));
        }

        await AdvanceAsync(cancellationToken).ConfigureAwait(false);
        return list;
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
        try
        {
            await _reader.DisposeAsync().ConfigureAwait(false);
            await _command.DisposeAsync().ConfigureAwait(false);
            if (_ownedConnection is not null)
            {
                await _ownedConnection.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            // Transacted path: release the pipeline's in-flight lease so the shared connection is free
            // for the next operation. Connection-per-op path: no lease (null).
            _lease?.Dispose();
        }
    }
}
