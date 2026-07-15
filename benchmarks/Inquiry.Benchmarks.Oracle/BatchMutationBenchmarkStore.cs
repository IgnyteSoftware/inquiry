using Inquiry.Entities;
using Inquiry.Stores;

namespace Inquiry.Benchmarks.Oracle;

[InquiryTable("InquiryBatchEvidence")]
public sealed class BatchMutationBenchmarkItem
{
    [InquiryKey]
    public int Id { get; set; }

    [InquiryColumn(Length = 100)]
    public string ValueText { get; set; } = string.Empty;
}

public partial class BatchMutationBenchmarkStore : InquiryStore<BatchMutationBenchmarkItem>
{
    [InquiryInsertAll]
    public partial Task<int> InsertAllAsync(IEnumerable<BatchMutationBenchmarkItem> items, CancellationToken cancellationToken = default);

    [InquiryUpdateAll]
    public partial Task<int> UpdateAllAsync(IEnumerable<BatchMutationBenchmarkItem> items, CancellationToken cancellationToken = default);

    [InquiryDeleteAll]
    public partial Task<int> DeleteAllAsync(IEnumerable<int> ids, CancellationToken cancellationToken = default);
}
