using Inquiry.Stores;

namespace Inquiry.Sqlite.Tests.Fixtures;

public abstract partial class DefaultedItemStore : InquiryStore<DefaultedItem>
{
    protected DefaultedItemStore(IInquiry inquiry) : base(inquiry) { }

    [InquiryInsert(ReturnEntity = true)]
    public abstract Task<DefaultedItem?> InsertReturningAsync(DefaultedItem item, CancellationToken cancellationToken = default);

    [InquiryUpdate(ReturnEntity = true)]
    public abstract Task<DefaultedItem?> UpdateReturningAsync(DefaultedItem item, CancellationToken cancellationToken = default);

    [InquiryUpsert(ReturnEntity = true)]
    public abstract Task<DefaultedItem?> UpsertReturningAsync(DefaultedItem item, CancellationToken cancellationToken = default);
}
