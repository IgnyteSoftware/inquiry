using Inquiry.Entities;
using Inquiry.Stores;

namespace Inquiry.Benchmarks;

[InquiryTable("InquiryBatchEvidence")]
public sealed class BatchMutationBenchmarkItem
{
    [InquiryKey] public int Id { get; set; }
    [InquiryColumn(Length = 100)] public string ValueText { get; set; } = string.Empty;
}

public partial class BatchMutationBenchmarkStore : InquiryStore<BatchMutationBenchmarkItem>
{
    [InquiryInsert]
    public partial Task<int> InsertAllAsync(IEnumerable<BatchMutationBenchmarkItem> items, CancellationToken cancellationToken = default);

    [InquiryUpdate]
    public partial Task<int> UpdateAllAsync(IEnumerable<BatchMutationBenchmarkItem> items, CancellationToken cancellationToken = default);

    [InquiryDelete, InquiryWhere("Id", Compare.In)]
    public partial Task<int> DeleteAllAsync(IEnumerable<int> ids, CancellationToken cancellationToken = default);
}
