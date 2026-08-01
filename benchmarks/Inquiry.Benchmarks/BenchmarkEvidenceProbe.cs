using Inquiry.Entities;
using Inquiry.Stores;

[assembly: Inquiry.InquiryDialect("Sqlite")]

namespace Inquiry.Benchmarks;

[InquiryTable("BenchmarkEvidenceProbe")]
internal sealed class BenchmarkEvidenceProbe
{
    [InquiryKey]
    public int Id { get; set; }
}

internal partial class BenchmarkEvidenceProbeStore : InquiryStore<BenchmarkEvidenceProbe>
{
    [InquirySelectOneByKey]
    public partial Task<BenchmarkEvidenceProbe?> GetAsync(int id, CancellationToken cancellationToken = default);
}
