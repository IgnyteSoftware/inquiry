using Inquiry.Commands;
using Inquiry.Materialization;
using System.Data;
using System.Data.Common;

namespace Inquiry.Transactions;

/// <summary>
/// An <see cref="IInquiry"/> wrapper exposed via <see cref="IInquiryTransaction.Inquiry"/>.
/// Forwards every call to the wrapped root <see cref="IInquiry"/> (so routing continues to go
/// through the ambient transactional pipeline) but checks the owning transaction's closed-state
/// first. After <c>CommitAsync</c> / <c>RollbackAsync</c> / <c>DisposeAsync</c>, every call on
/// the wrapper throws <see cref="ObjectDisposedException"/> — preventing the
/// silent-autocommit-after-close bug that would otherwise occur if <c>tx.Inquiry</c> returned
/// the root singleton (which has no idea the transaction has closed).
/// </summary>
/// <remarks>
/// The wrapper preserves performance: every method is a direct delegation to the inner root
/// <see cref="IInquiry"/> (typically <see cref="DefaultInquiry"/>), so the inner's allocation-free
/// fast paths (the TArgs+Action overloads, the struct-materializer overloads) are still
/// reached. The added cost is one branchy <c>ThrowIfClosed()</c> call per public method —
/// negligible against any real query/execute work.
/// </remarks>
internal sealed class TransactionScopedInquiry : IInquiry
{
    private readonly IInquiry _inner;
    private readonly Func<bool> _isClosed;
    private readonly string _closedTransactionTypeName;

    internal TransactionScopedInquiry(IInquiry inner, Func<bool> isClosed, string closedTransactionTypeName)
    {
        _inner = inner;
        _isClosed = isClosed;
        _closedTransactionTypeName = closedTransactionTypeName;
    }

    private void ThrowIfClosed()
    {
        if (_isClosed())
        {
            throw new ObjectDisposedException(
                _closedTransactionTypeName,
                "This Inquiry transaction's IInquiry handle has been committed, rolled back, or disposed. " +
                "Calls routed through tx.Inquiry after close are not allowed.");
        }
    }

    /// <inheritdoc />
    public bool ThrowOnConcurrencyConflict => _inner.ThrowOnConcurrencyConflict;

    // ---- Class-materializer overloads -------------------------------------------------

    /// <inheritdoc />
    public IAsyncEnumerable<TEntity> QueryAsync<TEntity>(string commandText, CancellationToken cancellationToken = default)
        where TEntity : class
    {
        ThrowIfClosed();
        return _inner.QueryAsync<TEntity>(commandText, cancellationToken);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<TEntity> QueryAsync<TEntity>(string commandText, object? parameters, CancellationToken cancellationToken = default)
        where TEntity : class
    {
        ThrowIfClosed();
        return _inner.QueryAsync<TEntity>(commandText, parameters, cancellationToken);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<TEntity> QueryAsync<TEntity>(InquiryCommand command, CancellationToken cancellationToken = default)
        where TEntity : class
    {
        ThrowIfClosed();
        return _inner.QueryAsync<TEntity>(command, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<TEntity>> QueryListAsync<TEntity>(string commandText, CancellationToken cancellationToken = default)
        where TEntity : class
    {
        ThrowIfClosed();
        return _inner.QueryListAsync<TEntity>(commandText, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<TEntity>> QueryListAsync<TEntity>(string commandText, object? parameters, CancellationToken cancellationToken = default)
        where TEntity : class
    {
        ThrowIfClosed();
        return _inner.QueryListAsync<TEntity>(commandText, parameters, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<TEntity>> QueryListAsync<TEntity>(InquiryCommand command, CancellationToken cancellationToken = default)
        where TEntity : class
    {
        ThrowIfClosed();
        return _inner.QueryListAsync<TEntity>(command, cancellationToken);
    }

    /// <inheritdoc />
    public Task<TEntity?> QuerySingleOrDefaultAsync<TEntity>(string commandText, CancellationToken cancellationToken = default)
        where TEntity : class
    {
        ThrowIfClosed();
        return _inner.QuerySingleOrDefaultAsync<TEntity>(commandText, cancellationToken);
    }

    /// <inheritdoc />
    public Task<TEntity?> QuerySingleOrDefaultAsync<TEntity>(string commandText, object? parameters, CancellationToken cancellationToken = default)
        where TEntity : class
    {
        ThrowIfClosed();
        return _inner.QuerySingleOrDefaultAsync<TEntity>(commandText, parameters, cancellationToken);
    }

    /// <inheritdoc />
    public Task<TEntity?> QuerySingleOrDefaultAsync<TEntity>(InquiryCommand command, CancellationToken cancellationToken = default)
        where TEntity : class
    {
        ThrowIfClosed();
        return _inner.QuerySingleOrDefaultAsync<TEntity>(command, cancellationToken);
    }

    // ---- Struct-materializer overloads ------------------------------------------------

    /// <inheritdoc />
    public IAsyncEnumerable<TEntity> QueryAsync<TEntity, TMaterializer>(InquiryCommand command, TMaterializer materializer, CancellationToken cancellationToken = default)
        where TEntity : class
        where TMaterializer : struct, IInquiryEntityMaterializer<TEntity>
    {
        ThrowIfClosed();
        return _inner.QueryAsync<TEntity, TMaterializer>(command, materializer, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<TEntity>> QueryListAsync<TEntity, TMaterializer>(InquiryCommand command, TMaterializer materializer, CancellationToken cancellationToken = default)
        where TEntity : class
        where TMaterializer : struct, IInquiryEntityMaterializer<TEntity>
    {
        ThrowIfClosed();
        return _inner.QueryListAsync<TEntity, TMaterializer>(command, materializer, cancellationToken);
    }

    /// <inheritdoc />
    public Task<TEntity?> QuerySingleOrDefaultAsync<TEntity, TMaterializer>(InquiryCommand command, TMaterializer materializer, CancellationToken cancellationToken = default)
        where TEntity : class
        where TMaterializer : struct, IInquiryEntityMaterializer<TEntity>
    {
        ThrowIfClosed();
        return _inner.QuerySingleOrDefaultAsync<TEntity, TMaterializer>(command, materializer, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<TEntity>> QueryListAsync<TEntity, TArgs, TMaterializer>(
        string commandText, TArgs args, Action<DbCommand, TArgs> bindParameters, TMaterializer materializer, CancellationToken cancellationToken = default)
        where TEntity : class
        where TMaterializer : struct, IInquiryEntityMaterializer<TEntity>
    {
        ThrowIfClosed();
        return _inner.QueryListAsync<TEntity, TArgs, TMaterializer>(commandText, args, bindParameters, materializer, cancellationToken);
    }

    /// <inheritdoc />
    public Task<TEntity?> QuerySingleOrDefaultAsync<TEntity, TArgs, TMaterializer>(
        string commandText, TArgs args, Action<DbCommand, TArgs> bindParameters, TMaterializer materializer, CancellationToken cancellationToken = default)
        where TEntity : class
        where TMaterializer : struct, IInquiryEntityMaterializer<TEntity>
    {
        ThrowIfClosed();
        return _inner.QuerySingleOrDefaultAsync<TEntity, TArgs, TMaterializer>(commandText, args, bindParameters, materializer, cancellationToken);
    }

    // ---- Execute ----------------------------------------------------------------------

    /// <inheritdoc />
    public Task<int> ExecuteAsync(string commandText, CancellationToken cancellationToken = default)
    {
        ThrowIfClosed();
        return _inner.ExecuteAsync(commandText, cancellationToken);
    }

    /// <inheritdoc />
    public Task<int> ExecuteAsync(string commandText, object? parameters, CancellationToken cancellationToken = default)
    {
        ThrowIfClosed();
        return _inner.ExecuteAsync(commandText, parameters, cancellationToken);
    }

    /// <inheritdoc />
    public Task<int> ExecuteAsync(InquiryCommand command, CancellationToken cancellationToken = default)
    {
        ThrowIfClosed();
        return _inner.ExecuteAsync(command, cancellationToken);
    }

    /// <inheritdoc />
    public Task<int> ExecuteAsync<TArgs>(string commandText, TArgs args, Action<DbCommand, TArgs> bindParameters, CancellationToken cancellationToken = default)
    {
        ThrowIfClosed();
        return _inner.ExecuteAsync(commandText, args, bindParameters, cancellationToken);
    }

    /// <inheritdoc />
    public Task<T> ExecuteScalarAsync<T>(InquiryCommand command, CancellationToken cancellationToken = default)
    {
        ThrowIfClosed();
        return _inner.ExecuteScalarAsync<T>(command, cancellationToken);
    }

    /// <inheritdoc />
    public Task<T> ExecuteScalarAsync<T, TArgs>(string commandText, TArgs args, Action<DbCommand, TArgs> bindParameters, CancellationToken cancellationToken = default)
    {
        ThrowIfClosed();
        return _inner.ExecuteScalarAsync<T, TArgs>(commandText, args, bindParameters, cancellationToken);
    }

    // ---- Transactions -----------------------------------------------------------------

    /// <inheritdoc />
    public Task<IInquiryTransaction> BeginTransactionAsync(IsolationLevel isolationLevel = IsolationLevel.ReadCommitted, CancellationToken cancellationToken = default)
    {
        ThrowIfClosed();
        return _inner.BeginTransactionAsync(isolationLevel, cancellationToken);
    }
}
