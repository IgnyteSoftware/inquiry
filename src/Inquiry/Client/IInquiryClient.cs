using System.Data.Common;

namespace Inquiry;

public interface IInquiryClient
{
    Task<TEntity?> FindAsync<TEntity, TKey>(TKey key, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TEntity>> SelectAsync<TEntity>(InquiryQuery<TEntity>? query = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TEntity>> SelectAsync<TEntity>(
        Func<InquiryQuery<TEntity>, InquiryQuery<TEntity>> configure,
        CancellationToken cancellationToken = default);

    Task<TEntity?> FirstOrDefaultAsync<TEntity>(InquiryQuery<TEntity> query, CancellationToken cancellationToken = default);

    Task<TEntity> SingleAsync<TEntity>(InquiryQuery<TEntity> query, CancellationToken cancellationToken = default);

    IAsyncEnumerable<TEntity> StreamAsync<TEntity>(InquiryQuery<TEntity> query, CancellationToken cancellationToken = default);

    Task<int> InsertAsync<TEntity>(TEntity entity, CancellationToken cancellationToken = default);

    Task<int> InsertManyAsync<TEntity>(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);

    Task<int> UpdateAsync<TEntity>(TEntity entity, CancellationToken cancellationToken = default);

    Task<int> UpdateOnlyAsync<TEntity>(TEntity entity, IReadOnlyList<string> properties, CancellationToken cancellationToken = default);

    Task<int> DeleteAsync<TEntity>(TEntity entity, CancellationToken cancellationToken = default);

    Task<int> DeleteByKeyAsync<TEntity, TKey>(TKey key, CancellationToken cancellationToken = default);

    Task<int> UpsertAsync<TEntity>(TEntity entity, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TEntity>> QueryAsync<TEntity>(string sql, object? parameters = null, CancellationToken cancellationToken = default);

    Task<TEntity?> QuerySingleOrDefaultAsync<TEntity>(string sql, object? parameters = null, CancellationToken cancellationToken = default);

    Task<int> ExecuteAsync(string sql, object? parameters = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TEntity>> QueryStoredProcedureAsync<TEntity>(
        string procedureName,
        object? parameters = null,
        CancellationToken cancellationToken = default);

    Task<TEntity?> QuerySingleOrDefaultStoredProcedureAsync<TEntity>(
        string procedureName,
        object? parameters = null,
        CancellationToken cancellationToken = default);

    Task<int> ExecuteStoredProcedureAsync(
        string procedureName,
        object? parameters = null,
        CancellationToken cancellationToken = default);

    Task<IInquiryTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);

    Task ExecuteInTransactionAsync(Func<IInquiryClient, CancellationToken, Task> callback, CancellationToken cancellationToken = default);
}

public interface IInquiryTransaction : IAsyncDisposable, IDisposable
{
    IInquiryClient Client { get; }

    Task CommitAsync(CancellationToken cancellationToken = default);

    Task RollbackAsync(CancellationToken cancellationToken = default);
}
