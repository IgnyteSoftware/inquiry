using Inquiry.Commands;
using System.Data;

namespace Inquiry.Transactions;

/// <summary>
/// Represents an active database transaction. All operations performed via this transaction —
/// the query / execute methods on this interface or a generated store resolved from DI in the
/// same async flow — share the same connection and the same <see cref="System.Data.Common.DbTransaction"/>.
/// Call <see cref="CommitAsync"/> to persist changes, or let the transaction roll back automatically
/// when disposed without committing.
/// </summary>
/// <remarks>
/// <para>
/// This interface exposes the full transactional query / execute surface directly — there is
/// no separate <c>IInquiry</c> handle property to access. Calls through these methods route
/// through the same physical connection / transaction the transaction owns, and fail fast with
/// <see cref="ObjectDisposedException"/> after the transaction has been committed, rolled back,
/// or disposed.
/// </para>
/// <para>
/// <see cref="BeginTransactionAsync"/> on this interface creates a savepoint inside the current
/// transaction. The savepoint can be selectively rolled back without affecting the outer
/// transaction's other work. Nesting is unbounded.
/// </para>
/// </remarks>
public interface IInquiryTransaction : IAsyncDisposable
{
    /// <summary>
    /// Gets the isolation level the underlying database transaction was opened with.
    /// </summary>
    IsolationLevel IsolationLevel { get; }

    /// <summary>
    /// Commits the transaction (or releases the savepoint, for a nested transaction).
    /// </summary>
    Task CommitAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Rolls back the transaction (or rolls back to the savepoint, for a nested transaction).
    /// </summary>
    Task RollbackAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Throws <see cref="ObjectDisposedException"/> if this transaction has been committed,
    /// rolled back, or disposed. Called by every query / execute method on this interface so a
    /// call through the transaction handle fails fast on use-after-close rather than silently
    /// routing through the non-transactional default pipeline.
    /// </summary>
    void ThrowIfClosed();

    // ---- Query / execute --------------------------------------------------------------
    //
    // All query/execute operations on a transaction go through these methods. No separate
    // IInquiry handle property is exposed — the entire surface lives here so there is one
    // and only one way to make a transactional call.

    /// <summary>Executes a SQL query and streams mapped entities, within this transaction.</summary>
    IAsyncEnumerable<TEntity> QueryAsync<TEntity>(
        string commandText,
        CancellationToken cancellationToken = default)
        where TEntity : class;

    /// <summary>Executes a SQL query with parameters and streams mapped entities, within this transaction.</summary>
    IAsyncEnumerable<TEntity> QueryAsync<TEntity>(
        string commandText,
        object? parameters,
        CancellationToken cancellationToken = default)
        where TEntity : class;

    /// <summary>Executes a SQL query and streams mapped entities, within this transaction.</summary>
    IAsyncEnumerable<TEntity> QueryAsync<TEntity>(
        InquiryCommand command,
        CancellationToken cancellationToken = default)
        where TEntity : class;

    /// <summary>Executes a SQL query and returns the buffered list of mapped entities, within this transaction.</summary>
    Task<IReadOnlyList<TEntity>> QueryListAsync<TEntity>(
        string commandText,
        CancellationToken cancellationToken = default)
        where TEntity : class;

    /// <summary>Executes a SQL query with parameters and returns the buffered list of mapped entities, within this transaction.</summary>
    Task<IReadOnlyList<TEntity>> QueryListAsync<TEntity>(
        string commandText,
        object? parameters,
        CancellationToken cancellationToken = default)
        where TEntity : class;

    /// <summary>Executes a SQL query and returns the buffered list of mapped entities, within this transaction.</summary>
    Task<IReadOnlyList<TEntity>> QueryListAsync<TEntity>(
        InquiryCommand command,
        CancellationToken cancellationToken = default)
        where TEntity : class;

    /// <summary>Executes a SQL query and returns the first mapped entity, or null when no row is returned, within this transaction.</summary>
    Task<TEntity?> QuerySingleOrDefaultAsync<TEntity>(
        string commandText,
        CancellationToken cancellationToken = default)
        where TEntity : class;

    /// <summary>Executes a SQL query with parameters and returns the first mapped entity, or null when no row is returned, within this transaction.</summary>
    Task<TEntity?> QuerySingleOrDefaultAsync<TEntity>(
        string commandText,
        object? parameters,
        CancellationToken cancellationToken = default)
        where TEntity : class;

    /// <summary>Executes a SQL query and returns the first mapped entity, or null when no row is returned, within this transaction.</summary>
    Task<TEntity?> QuerySingleOrDefaultAsync<TEntity>(
        InquiryCommand command,
        CancellationToken cancellationToken = default)
        where TEntity : class;

    /// <summary>Executes a SQL command and returns the affected row count, within this transaction.</summary>
    Task<int> ExecuteAsync(
        string commandText,
        CancellationToken cancellationToken = default);

    /// <summary>Executes a SQL command with parameters and returns the affected row count, within this transaction.</summary>
    Task<int> ExecuteAsync(
        string commandText,
        object? parameters,
        CancellationToken cancellationToken = default);

    /// <summary>Executes a SQL command and returns the affected row count, within this transaction.</summary>
    Task<int> ExecuteAsync(
        InquiryCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>Executes a command returning a single scalar value (COUNT/SUM/MIN/MAX/AVG), within this transaction.</summary>
    Task<T> ExecuteScalarAsync<T>(
        InquiryCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Begins a nested transaction (savepoint) inside this one. Commit releases the savepoint;
    /// rollback rolls back to it without affecting the outer transaction. Disposing without
    /// committing rolls back to the savepoint. Inherits the outer transaction's
    /// <see cref="IsolationLevel"/> — isolation cannot be changed mid-transaction.
    /// </summary>
    Task<IInquiryTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);
}
