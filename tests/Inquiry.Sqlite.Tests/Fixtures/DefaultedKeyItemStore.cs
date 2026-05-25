using Inquiry.Stores;

namespace Inquiry.Sqlite.Tests.Fixtures;

public abstract partial class DefaultedKeyItemStore : InquiryStore<DefaultedKeyItem>
{
    protected DefaultedKeyItemStore(IInquiry inquiry) : base(inquiry) { }

    [InquiryInsert(ReturnEntity = true)]
    public abstract Task<DefaultedKeyItem?> InsertReturningAsync(DefaultedKeyItem item, CancellationToken cancellationToken = default);

    [InquiryUpsert(ReturnEntity = true)]
    public abstract Task<DefaultedKeyItem?> UpsertReturningAsync(DefaultedKeyItem item, CancellationToken cancellationToken = default);
}
