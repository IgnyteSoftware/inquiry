using Inquiry.Stores;

namespace Inquiry.Sqlite.Tests.Fixtures;

public partial class DefaultedKeyItemStore : InquiryStore<DefaultedKeyItem>
{

    [InquiryInsert]
    public partial Task<DefaultedKeyItem?> InsertReturningAsync(DefaultedKeyItem item, CancellationToken cancellationToken = default);

    [InquiryUpsert]
    public partial Task<DefaultedKeyItem?> UpsertReturningAsync(DefaultedKeyItem item, CancellationToken cancellationToken = default);
}
