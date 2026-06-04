using Inquiry.Stores;

namespace Inquiry.Sqlite.Tests.Fixtures;

public partial class GeneratedItemStore : InquiryStore<GeneratedItem>
{

    [InquirySelectOneByKey]
    public partial Task<GeneratedItem?> SelectByKeyAsync(int? id, CancellationToken cancellationToken = default);

    [InquiryUpsert(ReturnEntity = true)]
    public partial Task<GeneratedItem?> UpsertReturningAsync(GeneratedItem item, CancellationToken cancellationToken = default);

    [InquirySelectAll]
    public partial Task<IReadOnlyList<GeneratedItem>> SelectAllAsync(CancellationToken cancellationToken = default);
}
