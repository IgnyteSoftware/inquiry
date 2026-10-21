using Inquiry.Northwind.Models;
using Inquiry.Paging;
using Inquiry.Stores;

namespace Inquiry.Northwind.Stores;

[InquirySpecification]
[InquiryWhere("Discontinued", OpenGroups = 1, Not = true)]
[InquiryWhere("UnitsInStock", Compare.LessThan, Or = true, CloseGroups = 1)]
[AttributeUsage(AttributeTargets.Method)]
public sealed class AvailableStockAttribute : Attribute
{
}

public partial class ProductStore : InquiryStore<Product>
{

    [InquirySelectAll]
    public partial Task<IReadOnlyList<Product>> SelectAllAsync(CancellationToken cancellationToken = default);

    [InquirySelectAll(OrderBy = "ProductName ASC")]
    public partial Task<IReadOnlyList<Product>> SelectAllOrderedByNameAsync(CancellationToken cancellationToken = default);

    [InquirySelectAll(OrderBy = "ProductID ASC", Paged = true)]
    public partial Task<IReadOnlyList<Product>> PageByIdAsync(int offset, int limit, CancellationToken cancellationToken = default);

    [InquiryKeysetPage("ProductID")]
    public partial Task<InquiryPage<Product, int>> KeysetByIdAsync(int? afterProductID, int pageSize, CancellationToken cancellationToken = default);

    [InquiryKeysetPage("ProductName", "ProductID")]
    public partial Task<InquiryPage<Product, (string, int)>> KeysetByNameThenIdAsync((string, int)? after, int pageSize, CancellationToken cancellationToken = default);

    [InquirySelectOneByKey]
    public partial Task<Product?> SelectByKeyAsync(int? productID, CancellationToken cancellationToken = default);

    [InquiryExists]
    [InquiryWhere("ProductID")]
    public partial Task<bool> ExistsByKeyAsync(int? productID, CancellationToken cancellationToken = default);

    [InquirySelectAllEager]
    public partial IAsyncEnumerable<Product> SelectAllWithCategoryAsync(CancellationToken cancellationToken = default);

    [InquirySelectOneByKeyEager]
    public partial Task<Product?> SelectByKeyWithCategoryAsync(int? productID, CancellationToken cancellationToken = default);

    [InquirySelectAllByField("CategoryID")]
    public partial Task<IReadOnlyList<Product>> SelectByCategoryAsync(int? categoryID, CancellationToken cancellationToken = default);

    [InquirySelectAllByPredicate]
    [InquiryWhere("UnitPrice", Compare.GreaterThanOrEqual)]
    [InquiryWhere("ProductName", Compare.Like)]
    public partial Task<IReadOnlyList<Product>> SearchAsync(decimal? minPrice, string namePattern, CancellationToken cancellationToken = default);

    [InquirySelectAllByPredicate]
    [InquiryWhere("UnitsInStock", Compare.Between)]
    public partial Task<IReadOnlyList<Product>> InStockRangeAsync(short? low, short? high, CancellationToken cancellationToken = default);

    [InquirySelectAllByPredicate]
    [InquiryWhere("CategoryID", Compare.In)]
    public partial Task<IReadOnlyList<Product>> InCategoriesAsync(IReadOnlyList<int> categoryID, CancellationToken cancellationToken = default);

    [InquirySelectAllByPredicate]
    [InquiryWhere("CategoryID", Compare.NotIn)]
    public partial Task<IReadOnlyList<Product>> NotInCategoriesAsync(IReadOnlyList<int> categoryID, CancellationToken cancellationToken = default);

    [InquirySelectAllByPredicate]
    [InquiryWhere("CategoryID", Compare.IsNull)]
    public partial Task<IReadOnlyList<Product>> WithoutCategoryAsync(CancellationToken cancellationToken = default);

    [InquirySelectAllByPredicate]
    [InquiryWhere("Discontinued", Compare.Equal)]
    [InquiryWhere("UnitsInStock", Compare.LessThan, Or = true)]
    public partial Task<IReadOnlyList<Product>> DiscontinuedOrLowStockAsync(bool discontinued, short? threshold, CancellationToken cancellationToken = default);

    [InquirySelectAllByPredicate]
    [AvailableStock]
    [InquiryWhere("ProductName", Optional = true)]
    public partial Task<IReadOnlyList<Product>> AvailableStockAsync(bool discontinued, short? threshold, string? productName, CancellationToken cancellationToken = default);

    [InquirySelectAllByPredicate(OrderBy = "ProductID ASC", Paged = true)]
    [InquiryWhere("Discontinued")]
    public partial Task<InquiryPagedResult<Product>> PageByDiscontinuedAsync(bool discontinued, int offset, int limit, CancellationToken cancellationToken = default);

    [InquiryInsert]
    public partial Task<int> InsertAsync(Product product, CancellationToken cancellationToken = default);

    [InquiryInsert]
    public partial Task<Product?> InsertReturningAsync(Product product, CancellationToken cancellationToken = default);

    [InquiryUpdate]
    public partial Task<bool> UpdateAsync(Product product, CancellationToken cancellationToken = default);

    [InquiryUpdate]
    public partial Task<Product?> UpdateReturningAsync(Product product, CancellationToken cancellationToken = default);

#if !INQUIRY_ORACLE_TESTS
    [InquiryUpsert]
    public partial Task<int> UpsertAsync(Product product, CancellationToken cancellationToken = default);

    [InquiryUpsert]
    public partial Task<Product?> UpsertReturningAsync(Product product, CancellationToken cancellationToken = default);
#endif

    [InquiryDelete]
    public partial Task<bool> DeleteByKeyAsync(int? productID, CancellationToken cancellationToken = default);

    // Aggregations + projection over the live Northwind data.
    [InquiryCount]
    public partial Task<long> CountAsync(CancellationToken cancellationToken = default);

    [InquiryAggregate(InquiryAggregateFunction.Max, "UnitPrice")]
    public partial Task<decimal?> MaxUnitPriceAsync(CancellationToken cancellationToken = default);

    [InquiryAggregate(InquiryAggregateFunction.Sum, "UnitPrice")]
    public partial Task<decimal?> SumUnitPriceAsync(CancellationToken cancellationToken = default);

    [InquiryAggregate(InquiryAggregateFunction.Sum, "UnitPrice")]
    [InquiryWhere("Discontinued")]
    public partial Task<decimal?> SumUnitPriceByDiscontinuedAsync(bool discontinued, CancellationToken cancellationToken = default);

    [InquiryUpdate]
    [InquirySet("UnitsInStock", "{UnitsInStock} + @amount")]
    [InquiryWhere("ProductID")]
    public partial Task<int> AddUnitsInStockAsync(short? amount, int? productID, CancellationToken cancellationToken = default);

    [InquirySelectAll]
    public partial Task<IReadOnlyList<ProductSummary>> SummariesAsync(CancellationToken cancellationToken = default);
}
