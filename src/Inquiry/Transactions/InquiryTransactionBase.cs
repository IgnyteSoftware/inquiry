using Inquiry.Commands;
using Inquiry.Materialization;
using Inquiry.Pipeline;
using System.Data;
using System.Data.Common;

namespace Inquiry.Transactions;

/// <summary>
/// Shared implementation of the query/execute methods on <see cref="IInquiryTransaction"/>.
/// The abstract Commit/Rollback/Dispose/IsolationLevel/ThrowIfClosed members are implemented by the
/// concrete <see cref="InquiryTransaction"/> and <see cref="SavepointInquiryTransaction"/>.
/// </summary>
/// <remarks>
/// Each handle captures its transactional pipeline. Calls do not depend on ambient state, which can
/// differ when a helper returns the handle to its caller. Every call checks closed state first.
/// </remarks>
internal abstract class InquiryTransactionBase : IInquiryTransaction
{
    private readonly DefaultInquiry _inner;
    private readonly TransactedInquiryRequestPipeline _pipeline;

    protected InquiryTransactionBase(DefaultInquiry inner, TransactedInquiryRequestPipeline pipeline)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    }

    public abstract IsolationLevel IsolationLevel { get; }
    public abstract DbConnection Connection { get; }
    public abstract DbTransaction Transaction { get; }
    public abstract Task CommitAsync(CancellationToken cancellationToken = default);
    public abstract Task RollbackAsync(CancellationToken cancellationToken = default);
    public abstract ValueTask DisposeAsync();
    public abstract void ThrowIfClosed();

    // ---- Class-materializer overloads -------------------------------------------------

    public IAsyncEnumerable<TEntity> QueryAsync<TEntity>(
        FormattableString commandText,
        CancellationToken cancellationToken = default)
        where TEntity : class
    {
        ThrowIfClosed();
        return QueryAsync<TEntity>(InquirySql.Sql(commandText), cancellationToken);
    }

    public IAsyncEnumerable<TEntity> QueryAsync<TEntity>(
        InquiryCommand command,
        CancellationToken cancellationToken = default)
        where TEntity : class
    {
        ThrowIfClosed();
        return QueryChecked<TEntity>(
            _pipeline.QueryAsync(command, _inner.GetMaterializer<TEntity>(), cancellationToken),
            cancellationToken);
    }

    private async IAsyncEnumerable<TEntity> QueryChecked<TEntity>(
        IAsyncEnumerable<TEntity> source,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        where TEntity : class
    {
        // QueryAsync is deferred: re-check at first enumeration so a sequence captured before a
        // commit/rollback/dispose cannot reach the provider after its handle closes.
        ThrowIfClosed();
        await foreach (var item in source.WithCancellation(cancellationToken).ConfigureAwait(false))
            yield return item;
    }

    public Task<IReadOnlyList<TEntity>> QueryListAsync<TEntity>(
        FormattableString commandText,
        CancellationToken cancellationToken = default)
        where TEntity : class
    {
        ThrowIfClosed();
        return QueryListAsync<TEntity>(InquirySql.Sql(commandText), cancellationToken);
    }

    public Task<IReadOnlyList<TEntity>> QueryListAsync<TEntity>(
        InquiryCommand command,
        CancellationToken cancellationToken = default)
        where TEntity : class
    {
        ThrowIfClosed();
        return _pipeline.QueryListAsync(command, _inner.GetMaterializer<TEntity>(), cancellationToken);
    }

    public Task<TEntity?> QuerySingleOrDefaultAsync<TEntity>(
        FormattableString commandText,
        CancellationToken cancellationToken = default)
        where TEntity : class
    {
        ThrowIfClosed();
        return QuerySingleOrDefaultAsync<TEntity>(InquirySql.Sql(commandText), cancellationToken);
    }

    public Task<TEntity?> QuerySingleOrDefaultAsync<TEntity>(
        InquiryCommand command,
        CancellationToken cancellationToken = default)
        where TEntity : class
    {
        ThrowIfClosed();
        return _pipeline.QuerySingleOrDefaultAsync(command, _inner.GetMaterializer<TEntity>(), cancellationToken);
    }

    // ---- Execute / scalar -------------------------------------------------------------

    public Task<int> ExecuteAsync(
        FormattableString commandText,
        CancellationToken cancellationToken = default)
    {
        ThrowIfClosed();
        return ExecuteAsync(InquirySql.Sql(commandText), cancellationToken);
    }

    public Task<int> ExecuteAsync(
        InquiryCommand command,
        CancellationToken cancellationToken = default)
    {
        ThrowIfClosed();
        return _pipeline.ExecuteAsync(command, cancellationToken);
    }

    public Task<T> ExecuteScalarAsync<T>(
        FormattableString commandText,
        CancellationToken cancellationToken = default)
    {
        ThrowIfClosed();
        return ExecuteScalarAsync<T>(InquirySql.Sql(commandText), cancellationToken);
    }

    public Task<T> ExecuteScalarAsync<T>(
        InquiryCommand command,
        CancellationToken cancellationToken = default)
    {
        ThrowIfClosed();
        return _pipeline.ExecuteScalarAsync<T>(command, cancellationToken);
    }

    // ---- Nested transaction (savepoint) ----------------------------------------------

    public Task<IInquiryTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfClosed();
        return _inner.BeginSavepointAsync(_pipeline, IsolationLevel, cancellationToken);
    }
}
