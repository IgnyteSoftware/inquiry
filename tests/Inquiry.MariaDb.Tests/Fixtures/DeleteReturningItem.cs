using Inquiry.Entities;
using Inquiry.Stores;

namespace Inquiry.MariaDb.Tests.Fixtures;

[InquiryTable("DeleteReturningItem")]
public sealed class DeleteReturningItem
{
    [InquiryKey(IsGenerated = true)] public long Id { get; set; }
    [InquiryColumn] public string Name { get; set; } = string.Empty;
    [InquiryConcurrencyToken] public int Version { get; set; }
}

public partial class DeleteReturningItemStore : InquiryStore<DeleteReturningItem>
{
    [InquiryInsert]
    public partial Task<DeleteReturningItem?> InsertAsync(DeleteReturningItem item, CancellationToken cancellationToken = default);

    [InquirySelectOneByKey]
    public partial Task<DeleteReturningItem?> ByIdAsync(long id, CancellationToken cancellationToken = default);

    [InquiryUpdate]
    public partial Task<bool> UpdateAsync(DeleteReturningItem item, CancellationToken cancellationToken = default);

    [InquiryDelete]
    public partial Task<DeleteReturningItem?> DeleteReturningAsync(DeleteReturningItem item, CancellationToken cancellationToken = default);
}
