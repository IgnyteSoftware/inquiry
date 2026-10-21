using Inquiry.Paging;
using Inquiry.Stores;

namespace Inquiry.Sample.Data;

[InquirySpecification]
[InquiryWhere(nameof(WorkspaceProduct.Name), Compare.Like, Optional = true)]
[InquiryWhere(nameof(WorkspaceProduct.Price), Compare.GreaterThanOrEqual, Optional = true)]
[InquiryWhere(nameof(WorkspaceProduct.Price), Compare.LessThanOrEqual, Optional = true)]
[InquiryWhere(nameof(WorkspaceProduct.Stock), Compare.GreaterThanOrEqual, Optional = true)]
[AttributeUsage(AttributeTargets.Method)]
public sealed class ProductSearchAttribute: Attribute;

[InquiryGenerateInterface]
public partial class WorkspaceProductStore: InquiryStore<WorkspaceProduct>
{
    [InquirySelectAllByPredicate(OrderBy = "Name ASC, Id ASC", Paged = true)]
    [ProductSearch]
    [InquiryWhere(nameof(WorkspaceProduct.CategoryId), Optional = true)]
    public partial Task<InquiryPagedResult<WorkspaceProduct>> NameAsync(
        string? name, decimal? minimum, decimal? maximum, int? minimumStock, int? categoryId,
        int offset, int limit, CancellationToken ct = default);

    [InquirySelectAllByPredicate(OrderBy = "Name ASC, Id ASC", Paged = true)]
    [ProductSearch]
    [InquiryWhere(nameof(WorkspaceProduct.CategoryId), Compare.IsNull)]
    public partial Task<InquiryPagedResult<WorkspaceProduct>> NameUnassignedAsync(
        string? name, decimal? minimum, decimal? maximum, int? minimumStock,
        int offset, int limit, CancellationToken ct = default);

    [InquirySelectAllByPredicate(OrderBy = "Name DESC, Id DESC", Paged = true)]
    [ProductSearch]
    [InquiryWhere(nameof(WorkspaceProduct.CategoryId), Optional = true)]
    public partial Task<InquiryPagedResult<WorkspaceProduct>> NameDescendingAsync(
        string? name, decimal? minimum, decimal? maximum, int? minimumStock, int? categoryId,
        int offset, int limit, CancellationToken ct = default);

    [InquirySelectAllByPredicate(OrderBy = "Name DESC, Id DESC", Paged = true)]
    [ProductSearch]
    [InquiryWhere(nameof(WorkspaceProduct.CategoryId), Compare.IsNull)]
    public partial Task<InquiryPagedResult<WorkspaceProduct>> NameDescendingUnassignedAsync(
        string? name, decimal? minimum, decimal? maximum, int? minimumStock,
        int offset, int limit, CancellationToken ct = default);

    [InquirySelectAllByPredicate(OrderBy = "Price ASC, Id ASC", Paged = true)]
    [ProductSearch]
    [InquiryWhere(nameof(WorkspaceProduct.CategoryId), Optional = true)]
    public partial Task<InquiryPagedResult<WorkspaceProduct>> PriceAsync(
        string? name, decimal? minimum, decimal? maximum, int? minimumStock, int? categoryId,
        int offset, int limit, CancellationToken ct = default);

    [InquirySelectAllByPredicate(OrderBy = "Price ASC, Id ASC", Paged = true)]
    [ProductSearch]
    [InquiryWhere(nameof(WorkspaceProduct.CategoryId), Compare.IsNull)]
    public partial Task<InquiryPagedResult<WorkspaceProduct>> PriceUnassignedAsync(
        string? name, decimal? minimum, decimal? maximum, int? minimumStock,
        int offset, int limit, CancellationToken ct = default);

    [InquirySelectAllByPredicate(OrderBy = "Price DESC, Id DESC", Paged = true)]
    [ProductSearch]
    [InquiryWhere(nameof(WorkspaceProduct.CategoryId), Optional = true)]
    public partial Task<InquiryPagedResult<WorkspaceProduct>> PriceDescendingAsync(
        string? name, decimal? minimum, decimal? maximum, int? minimumStock, int? categoryId,
        int offset, int limit, CancellationToken ct = default);

    [InquirySelectAllByPredicate(OrderBy = "Price DESC, Id DESC", Paged = true)]
    [ProductSearch]
    [InquiryWhere(nameof(WorkspaceProduct.CategoryId), Compare.IsNull)]
    public partial Task<InquiryPagedResult<WorkspaceProduct>> PriceDescendingUnassignedAsync(
        string? name, decimal? minimum, decimal? maximum, int? minimumStock,
        int offset, int limit, CancellationToken ct = default);

    [InquirySelectOneByKey]
    public partial Task<WorkspaceProduct?> FindAsync(int id, CancellationToken ct = default);

    [InquiryInsert]
    public partial Task<WorkspaceProduct?> CreateAsync(WorkspaceProduct product, CancellationToken ct = default);

    [InquiryInsert]
    public partial Task<int> SeedAsync(IEnumerable<WorkspaceProduct> products, CancellationToken ct = default);

    [InquiryUpdate]
    public partial Task<WorkspaceProduct?> SaveAsync(WorkspaceProduct product, CancellationToken ct = default);

    [InquiryDelete]
    public partial Task<bool> ArchiveAsync(WorkspaceProduct product, CancellationToken ct = default);
}

public partial class WorkspaceCategoryStore: InquiryStore<WorkspaceCategory>
{
    [InquirySelectAll(OrderBy = "Name ASC")]
    public partial Task<IReadOnlyList<WorkspaceCategory>> ListAsync(CancellationToken ct = default);

    [InquiryInsert]
    public partial Task<int> SeedAsync(IEnumerable<WorkspaceCategory> categories, CancellationToken ct = default);
}
