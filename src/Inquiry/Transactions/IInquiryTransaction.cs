namespace Inquiry.Transactions;

/// <summary>
/// Represents an active database transaction. All operations performed via <see cref="Inquiry"/>
/// share the same connection and transaction. Call <see cref="CommitAsync"/> to persist changes,
/// or let the transaction roll back automatically when disposed without committing.
/// </summary>
public interface IInquiryTransaction : IAsyncDisposable
{
    /// <summary>
    /// Gets an <see cref="IInquiry"/> instance whose operations run within this transaction.
    /// </summary>
    IInquiry Inquiry { get; }

    /// <summary>
    /// Commits the transaction.
    /// </summary>
    Task CommitAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Rolls back the transaction.
    /// </summary>
    Task RollbackAsync(CancellationToken cancellationToken = default);
}
