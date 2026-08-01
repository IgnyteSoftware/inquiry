using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Inquiry.Entities;
using Inquiry.Stores;

namespace Inquiry.FeatureCatalog;

/// <summary>
/// Fixture for <c>[InquiryBulkInsert]</c>: on SQL Server / PostgreSQL / MySQL the store method
/// streams through the provider's native bulk-copy API; on SQLite / Oracle the generator compiles
/// it down to the multi-row batch insert — both paths are exercised by the shared dialect suites.
/// </summary>
[InquiryTable("BulkItem")]
public sealed class BulkItem
{
    [InquiryKey(IsGenerated = true)]
    public long Id { get; set; }

    [InquiryColumn(Length = 100)]
    public string Category { get; set; } = string.Empty;

    [InquiryColumn]
    public decimal Amount { get; set; }

    [InquiryColumn(Length = 200)]
    public string? Note { get; set; }
}

public partial class BulkItemStore : InquiryStore<BulkItem>
{
    [InquiryBulkInsert]
    public partial Task<long> BulkInsertAsync(IEnumerable<BulkItem> items, CancellationToken cancellationToken = default);

    [InquiryCount]
    public partial Task<long> CountAsync(CancellationToken cancellationToken = default);

    [InquirySelectAllByField(nameof(BulkItem.Category))]
    public partial Task<IReadOnlyList<BulkItem>> ByCategoryAsync(string category, CancellationToken cancellationToken = default);
}
