using Inquiry.Northwind.Models;
using Inquiry.Stores;

namespace Inquiry.Northwind.Stores;

public abstract partial class ProductStore : InquiryStore<Product>
{
    protected ProductStore(IInquiry inquiry) : base(inquiry) { }

    [InquirySelectAll]
    public abstract Task<IReadOnlyList<Product>> SelectAllAsync(CancellationToken cancellationToken = default);

    [InquirySelectOneByKey]
    public abstract Task<Product?> SelectByKeyAsync(int? productID, CancellationToken cancellationToken = default);

    [InquirySelectAllEager]
    public abstract IAsyncEnumerable<Product> SelectAllWithCategoryAsync(CancellationToken cancellationToken = default);

    [InquirySelectOneByKeyEager]
    public abstract Task<Product?> SelectByKeyWithCategoryAsync(int? productID, CancellationToken cancellationToken = default);

    [InquirySelectAllByField("CategoryID")]
    public abstract Task<IReadOnlyList<Product>> SelectByCategoryAsync(int? categoryID, CancellationToken cancellationToken = default);

    [InquiryInsert]
    public abstract Task<int> InsertAsync(Product product, CancellationToken cancellationToken = default);

    [InquiryInsert(ReturnEntity = true)]
    public abstract Task<Product?> InsertReturningAsync(Product product, CancellationToken cancellationToken = default);

    [InquiryUpdate]
    public abstract Task<bool> UpdateAsync(Product product, CancellationToken cancellationToken = default);

    [InquiryUpdate(ReturnEntity = true)]
    public abstract Task<Product?> UpdateReturningAsync(Product product, CancellationToken cancellationToken = default);

    [InquiryUpsert]
    public abstract Task<int> UpsertAsync(Product product, CancellationToken cancellationToken = default);

    [InquiryUpsert(ReturnEntity = true)]
    public abstract Task<Product?> UpsertReturningAsync(Product product, CancellationToken cancellationToken = default);

    [InquiryDeleteOneByKey]
    public abstract Task<bool> DeleteByKeyAsync(int? productID, CancellationToken cancellationToken = default);
}
