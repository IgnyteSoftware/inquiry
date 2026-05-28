using Inquiry.Northwind.Models;
using Inquiry.Stores;

namespace Inquiry.Northwind.Stores;

public partial class ProductStore : InquiryStore<Product>
{

    [InquirySelectAll]
    public partial Task<IReadOnlyList<Product>> SelectAllAsync(CancellationToken cancellationToken = default);

    [InquirySelectOneByKey]
    public partial Task<Product?> SelectByKeyAsync(int? productID, CancellationToken cancellationToken = default);

    [InquirySelectAllEager]
    public partial IAsyncEnumerable<Product> SelectAllWithCategoryAsync(CancellationToken cancellationToken = default);

    [InquirySelectOneByKeyEager]
    public partial Task<Product?> SelectByKeyWithCategoryAsync(int? productID, CancellationToken cancellationToken = default);

    [InquirySelectAllByField("CategoryID")]
    public partial Task<IReadOnlyList<Product>> SelectByCategoryAsync(int? categoryID, CancellationToken cancellationToken = default);

    [InquiryInsert]
    public partial Task<int> InsertAsync(Product product, CancellationToken cancellationToken = default);

    [InquiryInsert(ReturnEntity = true)]
    public partial Task<Product?> InsertReturningAsync(Product product, CancellationToken cancellationToken = default);

    [InquiryUpdate]
    public partial Task<bool> UpdateAsync(Product product, CancellationToken cancellationToken = default);

    [InquiryUpdate(ReturnEntity = true)]
    public partial Task<Product?> UpdateReturningAsync(Product product, CancellationToken cancellationToken = default);

    [InquiryUpsert]
    public partial Task<int> UpsertAsync(Product product, CancellationToken cancellationToken = default);

    [InquiryUpsert(ReturnEntity = true)]
    public partial Task<Product?> UpsertReturningAsync(Product product, CancellationToken cancellationToken = default);

    [InquiryDeleteOneByKey]
    public partial Task<bool> DeleteByKeyAsync(int? productID, CancellationToken cancellationToken = default);
}
