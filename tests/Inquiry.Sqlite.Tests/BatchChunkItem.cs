using Inquiry.Entities;
using Inquiry.Stores;

namespace Inquiry.Sqlite.Tests;

[InquiryTable("BatchChunkItem")]
public sealed class BatchChunkItem
{
    [InquiryKey]
    public int Id { get; set; }

    [InquiryColumn("Value")]
    public string Value { get; set; } = string.Empty;
}

public partial class BatchChunkItemStore : InquiryStore<BatchChunkItem>
{
    [InquiryInsertAll]
    public partial Task<int> InsertAllAsync(IEnumerable<BatchChunkItem> items, CancellationToken cancellationToken = default);

    [InquiryUpdateAll]
    public partial Task<int> UpdateAllAsync(IEnumerable<BatchChunkItem> items, CancellationToken cancellationToken = default);

    [InquiryDeleteAll]
    public partial Task<int> DeleteAllAsync(IEnumerable<int> ids, CancellationToken cancellationToken = default);

    [InquiryCount]
    public partial Task<long> CountAsync(CancellationToken cancellationToken = default);
}
