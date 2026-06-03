using System.Data;
using System.Data.Common;

namespace Inquiry.Transactions;

/// <summary>
/// Default implementation of <see cref="IInquiryTransaction"/>.
/// Rolls back automatically on disposal unless <see cref="CommitAsync"/> has been called.
/// </summary>
internal sealed class InquiryTransaction : InquiryTransactionBase
{
    private readonly DbConnection _connection;
    private readonly DbTransaction _transaction;
    private readonly Action _onClose;
    private bool _closed;
    private bool _committed;
    private bool _disposed;

    internal InquiryTransaction(DbConnection connection, DbTransaction transaction, IInquiry inquiry, Action onClose)
        : base(inquiry)
    {
        _connection = connection;
        _transaction = transaction;
        _onClose = onClose;
    }

    /// <inheritdoc />
    public override IsolationLevel IsolationLevel => _transaction.IsolationLevel;

    /// <inheritdoc />
    public override void ThrowIfClosed()
    {
        // _closed is set on the first of Commit/Rollback/Dispose; _disposed is set by
        // DisposeAsync. The union covers every terminal state of this transaction handle.
        if (_closed || _disposed)
        {
            throw new ObjectDisposedException(
                nameof(InquiryTransaction),
                "This Inquiry transaction has already been committed, rolled back, or disposed. " +
                "Calls routed through the transaction handle (tx.X) after close are not allowed.");
        }
    }

    /// <inheritdoc />
    public override async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(InquiryTransaction));
        try
        {
            await _transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            _committed = true;
        }
        finally
        {
            // Whether the underlying CommitAsync succeeded or threw, this handle is now finished:
            // the slot's Pipeline must be nulled (so subsequent ambient store calls fall through
            // to the default pipeline instead of routing to a doomed transactional one), and
            // _closed must be set (so direct tx.X(...) calls trip ThrowIfClosed instead of
            // silently operating on a corrupted transaction). If commit threw, _committed stays
            // false and DisposeAsync will attempt a best-effort Rollback (already try/catch-wrapped).
            Close();
        }
    }

    /// <inheritdoc />
    public override async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(InquiryTransaction));
        try
        {
            await _transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            // Same as CommitAsync: even if the underlying RollbackAsync threw, the handle is done.
            Close();
        }
    }

    /// <inheritdoc />
    public override async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Close();

        if (!_committed)
        {
            try
            {
                await _transaction.RollbackAsync().ConfigureAwait(false);
            }
            catch
            {
                // Rollback on dispose is best-effort; swallow to avoid masking real exceptions.
            }
        }

        await _transaction.DisposeAsync().ConfigureAwait(false);
        await _connection.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Clears the ambient transactional pipeline (idempotent). Called on the first of
    /// Commit / Rollback / Dispose so that any straggler async work that runs after this
    /// transaction is closed falls back to the default (non-transactional) pipeline.
    /// </summary>
    private void Close()
    {
        if (_closed) return;
        _closed = true;
        _onClose();
    }
}
