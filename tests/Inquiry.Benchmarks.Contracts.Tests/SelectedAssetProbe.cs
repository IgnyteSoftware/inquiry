using Inquiry.Entities;
using Inquiry.Stores;

namespace Inquiry.Benchmarks.Contracts.Tests;

[InquiryTable("BenchmarkEvidenceProbe")]
public sealed class SelectedAssetProbe
{
    [InquiryKey]
    public int Id { get; set; }
}

public partial class SelectedAssetProbeStore : InquiryStore<SelectedAssetProbe>
{
    [InquirySelectOneByKey]
    public partial Task<SelectedAssetProbe?> GetAsync(int id, CancellationToken cancellationToken = default);
}
