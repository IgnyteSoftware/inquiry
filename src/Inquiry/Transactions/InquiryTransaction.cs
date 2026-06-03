using System.Data;
using System.Data.Common;

namespace Inquiry.Transactions;

/// <summary>
/// Default implementation of <see cref="IInquiryTransaction"/>.
/// Rolls back automatically on disposal unless <see cref="CommitAsync"/> has been called.
/// </summary>
internal sealed class InquiryTransaction : IInquiryTransaction
{
    private readonly DbConnection _connection;
    private readonly DbTransaction _transaction;
    private readonly Action _onClose;
    private bool _closed;
    private bool _committed;
    private bool _disposed;

    internal InquiryTransaction(DbConnection connection, DbTransaction transaction, IInquiry inquiry, Action onClose)
    {
        _connection = connection;
        _transaction = transaction;
        _onClose = onClose;
        Inquiry = inquiry;
    }

    /// <inheritdoc />
    public IInquiry Inquiry { get; }

    /// <inheritdoc />
    public IsolationLevel IsolationLevel => _transaction.IsolationLevel;

    /// <inheritdoc />
    public void ThrowIfClosed()
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
    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(InquiryTransaction));
        await _transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        _committed = true;
        Close();
    }

    /// <inheritdoc />
    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(InquiryTransaction));
        await _transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
        Close();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
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
