using Inquiry.Commands;
using Inquiry.Materialization;
using System.Data;
using System.Data.Common;

namespace Inquiry.Transactions;

/// <summary>
/// Shared implementation of the forwarding query/execute methods on <see cref="IInquiryTransaction"/>.
/// Holds the root <see cref="IInquiry"/> internally; the abstract Commit/Rollback/Dispose/IsolationLevel
/// /ThrowIfClosed members are implemented by the concrete <see cref="InquiryTransaction"/> and
/// <see cref="SavepointInquiryTransaction"/>.
/// </summary>
/// <remarks>
/// The root inquiry is held privately and never exposed — every transactional call on this handle
/// goes through one of the forwarding methods, which check closed-state first. This is what makes
/// use-after-close calls fail fast instead of silently routing through the non-transactional
/// pipeline.
/// </remarks>
internal abstract class InquiryTransactionBase : IInquiryTransaction
{
    private readonly IInquiry _inner;

    protected InquiryTransactionBase(IInquiry inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public abstract IsolationLevel IsolationLevel { get; }
    public abstract Task CommitAsync(CancellationToken cancellationToken = default);
    public abstract Task RollbackAsync(CancellationToken cancellationToken = default);
    public abstract ValueTask DisposeAsync();
    public abstract void ThrowIfClosed();

    // ---- Class-materializer overloads -------------------------------------------------

    public IAsyncEnumerable<TEntity> QueryAsync<TEntity>(
        string commandText,
        CancellationToken cancellationToken = default)
        where TEntity : class
    {
        ThrowIfClosed();
        return _inner.QueryAsync<TEntity>(commandText, cancellationToken);
    }

    public IAsyncEnumerable<TEntity> QueryAsync<TEntity>(
        string commandText,
        object? parameters,
        CancellationToken cancellationToken = default)
        where TEntity : class
    {
        ThrowIfClosed();
        return _inner.QueryAsync<TEntity>(commandText, parameters, cancellationToken);
    }

    public IAsyncEnumerable<TEntity> QueryAsync<TEntity>(
        InquiryCommand command,
        CancellationToken cancellationToken = default)
        where TEntity : class
    {
        ThrowIfClosed();
        return _inner.QueryAsync<TEntity>(command, cancellationToken);
    }

    public Task<IReadOnlyList<TEntity>> QueryListAsync<TEntity>(
        string commandText,
        CancellationToken cancellationToken = default)
        where TEntity : class
    {
        ThrowIfClosed();
        return _inner.QueryListAsync<TEntity>(commandText, cancellationToken);
    }

    public Task<IReadOnlyList<TEntity>> QueryListAsync<TEntity>(
        string commandText,
        object? parameters,
        CancellationToken cancellationToken = default)
        where TEntity : class
    {
        ThrowIfClosed();
        return _inner.QueryListAsync<TEntity>(commandText, parameters, cancellationToken);
    }

    public Task<IReadOnlyList<TEntity>> QueryListAsync<TEntity>(
        InquiryCommand command,
        CancellationToken cancellationToken = default)
        where TEntity : class
    {
        ThrowIfClosed();
        return _inner.QueryListAsync<TEntity>(command, cancellationToken);
    }

    public Task<TEntity?> QuerySingleOrDefaultAsync<TEntity>(
        string commandText,
        CancellationToken cancellationToken = default)
        where TEntity : class
    {
        ThrowIfClosed();
        return _inner.QuerySingleOrDefaultAsync<TEntity>(commandText, cancellationToken);
    }

    public Task<TEntity?> QuerySingleOrDefaultAsync<TEntity>(
        string commandText,
        object? parameters,
        CancellationToken cancellationToken = default)
        where TEntity : class
    {
        ThrowIfClosed();
        return _inner.QuerySingleOrDefaultAsync<TEntity>(commandText, parameters, cancellationToken);
    }

    public Task<TEntity?> QuerySingleOrDefaultAsync<TEntity>(
        InquiryCommand command,
        CancellationToken cancellationToken = default)
        where TEntity : class
    {
        ThrowIfClosed();
        return _inner.QuerySingleOrDefaultAsync<TEntity>(command, cancellationToken);
    }

    // ---- Execute / scalar -------------------------------------------------------------

    public Task<int> ExecuteAsync(
        string commandText,
        CancellationToken cancellationToken = default)
    {
        ThrowIfClosed();
        return _inner.ExecuteAsync(commandText, cancellationToken);
    }

    public Task<int> ExecuteAsync(
        string commandText,
        object? parameters,
        CancellationToken cancellationToken = default)
    {
        ThrowIfClosed();
        return _inner.ExecuteAsync(commandText, parameters, cancellationToken);
    }

    public Task<int> ExecuteAsync(
        InquiryCommand command,
        CancellationToken cancellationToken = default)
    {
        ThrowIfClosed();
        return _inner.ExecuteAsync(command, cancellationToken);
    }

    public Task<T> ExecuteScalarAsync<T>(
        InquiryCommand command,
        CancellationToken cancellationToken = default)
    {
        ThrowIfClosed();
        return _inner.ExecuteScalarAsync<T>(command, cancellationToken);
    }

    // ---- Nested transaction (savepoint) ----------------------------------------------

    public Task<IInquiryTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfClosed();
        // Inner BeginTransactionAsync detects the ambient slot and creates a savepoint. The
        // requested isolation level argument matches our own (a savepoint inherits the outer's
        // isolation; the level can't change mid-transaction).
        return _inner.BeginTransactionAsync(IsolationLevel, cancellationToken);
    }
}
