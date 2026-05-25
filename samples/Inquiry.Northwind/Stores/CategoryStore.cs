using Inquiry.Northwind.Models;
using Inquiry.Stores;

namespace Inquiry.Northwind.Stores;

public abstract partial class CategoryStore : InquiryStore<Category>
{
    protected CategoryStore(IInquiry inquiry) : base(inquiry) { }

    [InquirySelectAll]
    public abstract IAsyncEnumerable<Category> SelectAllAsync(CancellationToken cancellationToken = default);

    [InquirySelectOneByKey]
    public abstract Task<Category?> SelectByKeyAsync(int? categoryID, CancellationToken cancellationToken = default);

    [InquirySelectAllEager]
    public abstract IAsyncEnumerable<Category> SelectAllWithProductsAsync(CancellationToken cancellationToken = default);

    [InquirySelectOneByKeyEager]
    public abstract Task<Category?> SelectByKeyWithProductsAsync(int? categoryID, CancellationToken cancellationToken = default);

    [InquiryInsert]
    public abstract Task<int> InsertAsync(Category category, CancellationToken cancellationToken = default);

    [InquiryInsert(ReturnEntity = true)]
    public abstract Task<Category?> InsertReturningAsync(Category category, CancellationToken cancellationToken = default);

    [InquiryUpdate]
    public abstract Task<bool> UpdateAsync(Category category, CancellationToken cancellationToken = default);

    [InquiryDeleteOneByKey]
    public abstract Task<bool> DeleteByKeyAsync(int? categoryID, CancellationToken cancellationToken = default);
}
