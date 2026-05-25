using Inquiry.Stores;

namespace Inquiry.Sqlite.Tests.Fixtures;

public abstract partial class GeneratedItemStore : InquiryStore<GeneratedItem>
{
    protected GeneratedItemStore(IInquiry inquiry) : base(inquiry) { }

    [InquirySelectOneByKey]
    public abstract Task<GeneratedItem?> SelectByKeyAsync(int? id, CancellationToken cancellationToken = default);

    [InquiryUpsert(ReturnEntity = true)]
    public abstract Task<GeneratedItem?> UpsertReturningAsync(GeneratedItem item, CancellationToken cancellationToken = default);
}
