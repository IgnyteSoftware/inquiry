using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Inquiry.Entities;
using Inquiry.Stores;

namespace Inquiry.FeatureCatalog;

[InquiryTable("Sale")]
public sealed class SaleRow
{
    [InquiryKey(IsGenerated = true)]
    public long Id { get; set; }

    [InquiryColumn]
    public string Category { get; set; } = string.Empty;

    [InquiryColumn]
    public decimal Amount { get; set; }
}

public partial class SaleRowStore : InquiryStore<SaleRow>
{
    [InquiryInsert]
    public partial Task<int> InsertAsync(SaleRow row, CancellationToken cancellationToken = default);
}

[InquiryView("v_CategoryTotals")]
public sealed class CategoryTotal
{
    [InquiryColumn("Category")]
    public string Category { get; set; } = string.Empty;

    [InquiryColumn("SaleCount")]
    public int SaleCount { get; set; }

    [InquiryColumn("TotalAmount")]
    public decimal TotalAmount { get; set; }
}

public partial class CategoryTotalStore : InquiryStore<CategoryTotal>
{
    [InquirySelectAll]
    public partial Task<IReadOnlyList<CategoryTotal>> AllAsync(CancellationToken cancellationToken = default);

    [InquirySelectAllByField(nameof(CategoryTotal.Category))]
    public partial Task<IReadOnlyList<CategoryTotal>> ByCategoryAsync(string category, CancellationToken cancellationToken = default);
}
