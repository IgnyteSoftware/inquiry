using Inquiry.Stores;

namespace Inquiry.MariaDb.Tests.Fixtures;

public partial class GeneratedItemStore : InquiryStore<GeneratedItem>
{
    [InquiryUpsert] public partial Task<int> UpsertAsync(GeneratedItem item, CancellationToken ct = default);
    [InquiryUpsert] public partial Task<GeneratedItem?> UpsertReturningAsync(GeneratedItem item, CancellationToken ct = default);
    [InquirySelectOneByKey] public partial Task<GeneratedItem?> SelectByKeyAsync(long? id, CancellationToken ct = default);
    [InquirySelectAll] public partial Task<IReadOnlyList<GeneratedItem>> SelectAllAsync(CancellationToken ct = default);
}
