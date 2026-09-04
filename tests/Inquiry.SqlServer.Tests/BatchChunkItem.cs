using Inquiry.Entities;
using Inquiry.Stores;

namespace Inquiry.SqlServer.Tests;

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

    [InquiryDelete, InquiryWhere("Id", Compare.In)]
    public partial Task<int> DeleteAllAsync(IEnumerable<int> ids, CancellationToken cancellationToken = default);

    [InquiryCount]
    public partial Task<long> CountAsync(CancellationToken cancellationToken = default);
}

[InquiryTable("WideBatchChunkItem")]
public sealed class WideBatchChunkItem
{
    [InquiryKey] public int C0 { get; set; }
    [InquiryColumn] public int C1 { get; set; }
    [InquiryColumn] public int C2 { get; set; }
    [InquiryColumn] public int C3 { get; set; }
    [InquiryColumn] public int C4 { get; set; }
    [InquiryColumn] public int C5 { get; set; }
    [InquiryColumn] public int C6 { get; set; }
    [InquiryColumn] public int C7 { get; set; }
    [InquiryColumn] public int C8 { get; set; }
    [InquiryColumn] public int C9 { get; set; }
}

public partial class WideBatchChunkItemStore : InquiryStore<WideBatchChunkItem>
{
    [InquiryInsertAll]
    public partial Task<int> InsertAllAsync(
        IEnumerable<WideBatchChunkItem> items,
        CancellationToken cancellationToken = default);

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
    [InquiryInsertAll]
    public partial Task<int> InsertAllAsync(
        IEnumerable<DefaultOnlyBatchItem> items,
        CancellationToken cancellationToken = default);

    [InquiryCount]
    public partial Task<long> CountAsync(CancellationToken cancellationToken = default);
}
