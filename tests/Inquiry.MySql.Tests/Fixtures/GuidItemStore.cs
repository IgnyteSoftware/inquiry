using Inquiry.Stores;

namespace Inquiry.MySql.Tests.Fixtures;

public partial class GuidItemStore : InquiryStore<GuidItem>
{
    [InquiryInsert] public partial Task<GuidItem?> InsertReturningAsync(GuidItem item, CancellationToken ct = default);
    [InquiryUpsert] public partial Task<int> UpsertAsync(GuidItem item, CancellationToken ct = default);
    [InquiryUpsert] public partial Task<GuidItem?> UpsertReturningAsync(GuidItem item, CancellationToken ct = default);
    [InquirySelectOneByKey] public partial Task<GuidItem?> SelectByKeyAsync(Guid? id, CancellationToken ct = default);
    [InquirySelectAll] public partial Task<IReadOnlyList<GuidItem>> SelectAllAsync(CancellationToken ct = default);
}
