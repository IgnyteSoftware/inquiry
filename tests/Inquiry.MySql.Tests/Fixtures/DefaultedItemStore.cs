using Inquiry.Stores;

namespace Inquiry.MySql.Tests.Fixtures;

public partial class DefaultedItemStore : InquiryStore<DefaultedItem>
{
    [InquiryInsert]
    public partial Task<DefaultedItem?> InsertReturningAsync(DefaultedItem item, CancellationToken cancellationToken = default);

    [InquiryUpdate]
    public partial Task<DefaultedItem?> UpdateReturningAsync(DefaultedItem item, CancellationToken cancellationToken = default);

    [InquiryUpsert]
    public partial Task<DefaultedItem?> UpsertReturningAsync(DefaultedItem item, CancellationToken cancellationToken = default);

    [InquirySelectOneByKey]
    public partial Task<DefaultedItem?> SelectByKeyAsync(long key, CancellationToken cancellationToken = default);
}
