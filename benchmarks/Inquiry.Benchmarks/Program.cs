using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Inquiry;

BenchmarkRunner.Run<InquiryBaselineBenchmarks>();

[MemoryDiagnoser]
public class InquiryBaselineBenchmarks
{
    private readonly InquiryMetadataRegistry _registry = new();
    private readonly IInquiryCommandFactory _factory = InquirySqliteProvider.Instance.CommandFactory;
    private IInquiryEntityDescriptor<BenchmarkUser> _descriptor = null!;

    [GlobalSetup]
    public void Setup()
    {
        _descriptor = _registry.GetDescriptor<BenchmarkUser>();
    }

    [Benchmark]
    public IInquiryEntityDescriptor<BenchmarkUser> MetadataCacheHit()
    {
        return _registry.GetDescriptor<BenchmarkUser>();
    }

    [Benchmark]
    public string BuildFindSql()
    {
        return _factory.BuildFind(_descriptor).CommandText;
    }

    [Benchmark]
    public string BuildInsertSql()
    {
        return _factory.BuildInsert(_descriptor).CommandText;
    }

    [Benchmark]
    public string BuildUpdateSql()
    {
        return _factory.BuildUpdate(_descriptor).CommandText;
    }

    [InquiryTable("benchmark_users")]
    public sealed class BenchmarkUser
    {
        [InquiryKey]
        [InquiryColumn("id")]
        public Guid Id { get; set; }

        [InquiryColumn("email")]
        public string Email { get; set; } = string.Empty;

        [InquiryColumn("display_name")]
        public string? DisplayName { get; set; }

        [InquiryColumn("created_at")]
        public DateTimeOffset CreatedAt { get; set; }

        [InquiryConcurrencyToken]
        [InquiryColumn("version")]
        public int Version { get; set; }
    }
}
