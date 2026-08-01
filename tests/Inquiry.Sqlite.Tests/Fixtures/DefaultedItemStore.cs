using Inquiry.Stores;

namespace Inquiry.Sqlite.Tests.Fixtures;

public partial class DefaultedItemStore : InquiryStore<DefaultedItem>
{

    [InquiryInsert(ReturnEntity = true)]
    public partial Task<DefaultedItem?> InsertReturningAsync(DefaultedItem item, CancellationToken cancellationToken = default);

    [InquiryUpdate(ReturnEntity = true)]
    public partial Task<DefaultedItem?> UpdateReturningAsync(DefaultedItem item, CancellationToken cancellationToken = default);

    [InquiryUpsert(ReturnEntity = true)]
    public partial Task<DefaultedItem?> UpsertReturningAsync(DefaultedItem item, CancellationToken cancellationToken = default);
}
