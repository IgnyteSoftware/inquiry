using Inquiry.Stores;
using System.Collections.Generic;

namespace Inquiry.Sqlite.Tests.Fixtures;

public abstract partial class ProductStore : InquiryStore<Product>
{
    protected ProductStore(IInquiry inquiry) : base(inquiry) { }

    [InquirySelectAll]
    public abstract IAsyncEnumerable<Product> SelectAllAsync(CancellationToken cancellationToken = default);

    [InquirySelectOneByKey]
    public abstract Task<Product?> SelectByKeyAsync(Guid key, CancellationToken cancellationToken = default);

    [InquirySelectAllByField("CategoryKey")]
    public abstract IAsyncEnumerable<Product> SelectByCategoryAsync(Guid categoryKey, CancellationToken cancellationToken = default);

    [InquiryInsert]
    public abstract Task<int> InsertAsync(Product product, CancellationToken cancellationToken = default);

    [InquiryUpdate]
    public abstract Task<bool> UpdateAsync(Product product, CancellationToken cancellationToken = default);

    [InquiryUpsert]
    public abstract Task<int> UpsertAsync(Product product, CancellationToken cancellationToken = default);

    [InquiryBulkInsert]
    public abstract Task<int> BulkInsertAsync(IEnumerable<Product> products, CancellationToken cancellationToken = default);

    [InquiryBulkUpdate]
    public abstract Task<int> BulkUpdateAsync(IEnumerable<Product> products, CancellationToken cancellationToken = default);

    [InquiryBulkDelete]
    public abstract Task<int> BulkDeleteAsync(IEnumerable<Guid> keys, CancellationToken cancellationToken = default);

    [InquiryDeleteOneByKey]
    public abstract Task<bool> DeleteByKeyAsync(Guid key, CancellationToken cancellationToken = default);
}
