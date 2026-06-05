using Inquiry.Stores;

namespace Inquiry.MySql.Tests.Fixtures;

public partial class GeneratedItemStore : InquiryStore<GeneratedItem>
{
    [InquiryUpsert] public partial Task<int> UpsertAsync(GeneratedItem item, CancellationToken ct = default);
    [InquiryUpsert(ReturnEntity = true)] public partial Task<GeneratedItem?> UpsertReturningAsync(GeneratedItem item, CancellationToken ct = default);
    [InquirySelectOneByKey] public partial Task<GeneratedItem?> SelectByKeyAsync(long? id, CancellationToken ct = default);
    [InquirySelectAll] public partial Task<IReadOnlyList<GeneratedItem>> SelectAllAsync(CancellationToken ct = default);
}
