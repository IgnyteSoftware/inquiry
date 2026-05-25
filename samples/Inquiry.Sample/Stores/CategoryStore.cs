using Inquiry.Sample.Models;
using Inquiry.Stores;

namespace Inquiry.Sample.Stores;

public abstract partial class CategoryStore : InquiryStore<Category>
{
    protected CategoryStore(IInquiry inquiry) : base(inquiry) { }

    [InquirySelectAll]
    public abstract IAsyncEnumerable<Category> SelectAllAsync(CancellationToken cancellationToken = default);

    [InquirySelectOneByKey]
    public abstract Task<Category?> SelectByKeyAsync(Guid key, CancellationToken cancellationToken = default);

    [InquirySelectOneByKeyEager]
    public abstract Task<Category?> SelectByKeyWithProductsAsync(Guid key, CancellationToken cancellationToken = default);

    [InquirySelectAllEager]
    public abstract IAsyncEnumerable<Category> SelectAllWithProductsAsync(CancellationToken cancellationToken = default);

    [InquiryInsert]
    public abstract Task<int> InsertAsync(Category category, CancellationToken cancellationToken = default);

    [InquiryUpdate]
    public abstract Task<bool> UpdateAsync(Category category, CancellationToken cancellationToken = default);

    [InquiryDeleteOneByKey]
    public abstract Task<bool> DeleteByKeyAsync(Guid key, CancellationToken cancellationToken = default);
}
