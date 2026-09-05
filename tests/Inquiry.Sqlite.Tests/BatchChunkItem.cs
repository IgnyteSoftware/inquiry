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
    [InquiryInsert]
    public partial Task<int> InsertAllAsync(IEnumerable<BatchChunkItem> items, CancellationToken cancellationToken = default);

    [InquiryUpdate]
    public partial Task<int> UpdateAllAsync(IEnumerable<BatchChunkItem> items, CancellationToken cancellationToken = default);

    [InquiryDelete, InquiryWhere("Id", Compare.In)]
    public partial Task<int> DeleteAllAsync(IEnumerable<int> ids, CancellationToken cancellationToken = default);

    [InquiryCount]
    public partial Task<long> CountAsync(CancellationToken cancellationToken = default);
}

[InquiryTable("DefaultOnlyBatchItem")]
public sealed class DefaultOnlyBatchItem
{
    [InquiryKey(IsGenerated = true)]
    public int Id { get; set; }
}

public partial class DefaultOnlyBatchItemStore : InquiryStore<DefaultOnlyBatchItem>
{
    [InquiryInsert]
    public partial Task<int> InsertAllAsync(
        IEnumerable<DefaultOnlyBatchItem> items,
        CancellationToken cancellationToken = default);

    [InquiryCount]
    public partial Task<long> CountAsync(CancellationToken cancellationToken = default);
}
