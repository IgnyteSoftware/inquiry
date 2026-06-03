using Inquiry.Commands;
using System.Data;

namespace Inquiry.Transactions;

/// <summary>
/// Represents an active database transaction. All operations performed via this transaction —
/// whether through the convenience overloads on this interface, through <see cref="Inquiry"/>,
/// or through a generated store resolved from DI — share the same connection and transaction.
/// Call <see cref="CommitAsync"/> to persist changes, or let the transaction roll back
/// automatically when disposed without committing.
/// </summary>
/// <remarks>
/// <para>
/// The query / execute methods on this interface are forwarding overloads: they delegate to the
/// matching method on <see cref="Inquiry"/>. They exist purely for ergonomics — so a caller can
/// write <c>await tx.ExecuteAsync(...)</c> instead of <c>await tx.Inquiry.ExecuteAsync(...)</c>.
/// The <see cref="Inquiry"/> property remains the canonical handle for advanced overloads (e.g.
/// the struct-materializer paths generated stores use internally).
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
    /// Gets an <see cref="IInquiry"/> instance whose operations run within this transaction.
    /// </summary>
    /// <remarks>
    /// On the built-in <see cref="DefaultInquiry"/> implementation, this is the same singleton
    /// IInquiry registered in DI — generated stores resolved from DI also see the transaction
    /// via the ambient mechanism without needing this handle. Use this property for advanced
    /// overloads that aren't surfaced on <see cref="IInquiryTransaction"/> directly (e.g. the
    /// struct-materializer / TArgs binder paths).
    /// </remarks>
    IInquiry Inquiry { get; }

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
    /// rolled back, or disposed. Called by every forwarding query/execute method on this
    /// interface so a call routed through the transaction handle (<c>tx.X(...)</c>) fails
    /// fast on use-after-close rather than silently routing through the non-transactional
    /// default pipeline. The default implementation is a no-op so custom test-double
    /// implementations of <see cref="IInquiryTransaction"/> stay source-compatible; the
    /// built-in implementations override it to track their closed state.
    /// </summary>
    /// <remarks>
    /// Note that this only protects the forwarding methods on this interface
    /// (<c>tx.QueryAsync</c> / <c>tx.ExecuteAsync</c> / etc.). Calls routed via the
    /// <see cref="Inquiry"/> property (<c>tx.Inquiry.X(...)</c>) or via a generated store
    /// resolved from DI are calls on the root <see cref="IInquiry"/> singleton — they
    /// remain reachable after this transaction closes, and will silently fall through to
    /// the non-transactional default pipeline (this is intentional so legitimate post-tx
    /// work in the same scope keeps working). Prefer the forwarding methods on this
    /// interface when you want use-after-close protection.
    /// </remarks>
    void ThrowIfClosed()
    {
    }

    // ---- Forwarding overloads ---------------------------------------------------------
    //
    // Default-interface methods that delegate to Inquiry. Custom IInquiryTransaction
    // implementations (e.g. test doubles) only have to provide the four required members
    // above — these come for free. Each method calls ThrowIfClosed() first so the
    // built-in implementations can fail-fast on use-after-close.

    /// <summary>Executes a SQL query and streams mapped entities, within this transaction.</summary>
    IAsyncEnumerable<TEntity> QueryAsync<TEntity>(
        string commandText,
        CancellationToken cancellationToken = default)
        where TEntity : class
    {
        ThrowIfClosed();
        return Inquiry.QueryAsync<TEntity>(commandText, cancellationToken);
    }

    /// <summary>Executes a SQL query with parameters and streams mapped entities, within this transaction.</summary>
    IAsyncEnumerable<TEntity> QueryAsync<TEntity>(
        string commandText,
        object? parameters,
        CancellationToken cancellationToken = default)
        where TEntity : class
    {
        ThrowIfClosed();
        return Inquiry.QueryAsync<TEntity>(commandText, parameters, cancellationToken);
    }

    /// <summary>Executes a SQL query and streams mapped entities, within this transaction.</summary>
    IAsyncEnumerable<TEntity> QueryAsync<TEntity>(
        InquiryCommand command,
        CancellationToken cancellationToken = default)
        where TEntity : class
    {
        ThrowIfClosed();
        return Inquiry.QueryAsync<TEntity>(command, cancellationToken);
    }

    /// <summary>Executes a SQL query and returns the buffered list of mapped entities, within this transaction.</summary>
    Task<IReadOnlyList<TEntity>> QueryListAsync<TEntity>(
        string commandText,
        CancellationToken cancellationToken = default)
        where TEntity : class
    {
        ThrowIfClosed();
        return Inquiry.QueryListAsync<TEntity>(commandText, cancellationToken);
    }

    /// <summary>Executes a SQL query with parameters and returns the buffered list of mapped entities, within this transaction.</summary>
    Task<IReadOnlyList<TEntity>> QueryListAsync<TEntity>(
        string commandText,
        object? parameters,
        CancellationToken cancellationToken = default)
        where TEntity : class
    {
        ThrowIfClosed();
        return Inquiry.QueryListAsync<TEntity>(commandText, parameters, cancellationToken);
    }

    /// <summary>Executes a SQL query and returns the buffered list of mapped entities, within this transaction.</summary>
    Task<IReadOnlyList<TEntity>> QueryListAsync<TEntity>(
        InquiryCommand command,
        CancellationToken cancellationToken = default)
        where TEntity : class
    {
        ThrowIfClosed();
        return Inquiry.QueryListAsync<TEntity>(command, cancellationToken);
    }

    /// <summary>Executes a SQL query and returns the first mapped entity, or null when no row is returned, within this transaction.</summary>
    Task<TEntity?> QuerySingleOrDefaultAsync<TEntity>(
        string commandText,
        CancellationToken cancellationToken = default)
        where TEntity : class
    {
        ThrowIfClosed();
        return Inquiry.QuerySingleOrDefaultAsync<TEntity>(commandText, cancellationToken);
    }

    /// <summary>Executes a SQL query with parameters and returns the first mapped entity, or null when no row is returned, within this transaction.</summary>
    Task<TEntity?> QuerySingleOrDefaultAsync<TEntity>(
        string commandText,
        object? parameters,
        CancellationToken cancellationToken = default)
        where TEntity : class
    {
        ThrowIfClosed();
        return Inquiry.QuerySingleOrDefaultAsync<TEntity>(commandText, parameters, cancellationToken);
    }

    /// <summary>Executes a SQL query and returns the first mapped entity, or null when no row is returned, within this transaction.</summary>
    Task<TEntity?> QuerySingleOrDefaultAsync<TEntity>(
        InquiryCommand command,
        CancellationToken cancellationToken = default)
        where TEntity : class
    {
        ThrowIfClosed();
        return Inquiry.QuerySingleOrDefaultAsync<TEntity>(command, cancellationToken);
    }

    /// <summary>Executes a SQL command and returns the affected row count, within this transaction.</summary>
    Task<int> ExecuteAsync(
        string commandText,
        CancellationToken cancellationToken = default)
    {
        ThrowIfClosed();
        return Inquiry.ExecuteAsync(commandText, cancellationToken);
    }

    /// <summary>Executes a SQL command with parameters and returns the affected row count, within this transaction.</summary>
    Task<int> ExecuteAsync(
        string commandText,
        object? parameters,
        CancellationToken cancellationToken = default)
    {
        ThrowIfClosed();
        return Inquiry.ExecuteAsync(commandText, parameters, cancellationToken);
    }

    /// <summary>Executes a SQL command and returns the affected row count, within this transaction.</summary>
    Task<int> ExecuteAsync(
        InquiryCommand command,
        CancellationToken cancellationToken = default)
    {
        ThrowIfClosed();
        return Inquiry.ExecuteAsync(command, cancellationToken);
    }

    /// <summary>Executes a command returning a single scalar value (COUNT/SUM/MIN/MAX/AVG), within this transaction.</summary>
    Task<T> ExecuteScalarAsync<T>(
        InquiryCommand command,
        CancellationToken cancellationToken = default)
    {
        ThrowIfClosed();
        return Inquiry.ExecuteScalarAsync<T>(command, cancellationToken);
    }

    /// <summary>
    /// Begins a nested transaction (savepoint) inside this one. Commit releases the savepoint;
    /// rollback rolls back to it without affecting the outer transaction. Disposing without
    /// committing rolls back to the savepoint.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IInquiryTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfClosed();
        return Inquiry.BeginTransactionAsync(IsolationLevel, cancellationToken);
    }
}
