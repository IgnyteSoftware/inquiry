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
    private bool _committed;
    private bool _disposed;

    internal InquiryTransaction(DbConnection connection, DbTransaction transaction, IInquiry transactedInquiry)
    {
        _connection = connection;
        _transaction = transaction;
        Inquiry = transactedInquiry;
    }

    /// <inheritdoc />
    public IInquiry Inquiry { get; }

    /// <inheritdoc />
    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(InquiryTransaction));
        await _transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        _committed = true;
    }

    /// <inheritdoc />
    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(InquiryTransaction));
        await _transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

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
}
