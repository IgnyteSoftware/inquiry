using Inquiry.Northwind.Models;
using Inquiry.Stores;

namespace Inquiry.Northwind.Stores;

public partial class CategoryStore : InquiryStore<Category>
{

    [InquirySelectAll]
    public partial IAsyncEnumerable<Category> SelectAllAsync(CancellationToken cancellationToken = default);

    [InquirySelectOneByKey]
    public partial Task<Category?> SelectByKeyAsync(int? categoryID, CancellationToken cancellationToken = default);

    [InquirySelectAllEager]
    public partial IAsyncEnumerable<Category> SelectAllWithProductsAsync(CancellationToken cancellationToken = default);

    [InquirySelectOneByKeyEager]
    public partial Task<Category?> SelectByKeyWithProductsAsync(int? categoryID, CancellationToken cancellationToken = default);

    [InquiryInsert]
    public partial Task<int> InsertAsync(Category category, CancellationToken cancellationToken = default);

    [InquiryInsert]
    public partial Task<Category?> InsertReturningAsync(Category category, CancellationToken cancellationToken = default);

    [InquiryUpdate]
    public partial Task<bool> UpdateAsync(Category category, CancellationToken cancellationToken = default);

    [InquiryDelete]
    public partial Task<bool> DeleteByKeyAsync(int? categoryID, CancellationToken cancellationToken = default);
}
