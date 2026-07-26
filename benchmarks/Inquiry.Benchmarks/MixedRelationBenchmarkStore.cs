using Inquiry.Entities;
using Inquiry.Stores;

namespace Inquiry.Benchmarks;

// #70: the 1-parent + 2-relations eager shape. MixedBenchPost carries both a to-one reference
// (Author) and a to-many collection (Tags), so one eager load batches three SELECTs into a single
// command. Declared here rather than reused from the test FeatureCatalog, which is test-only sources.

[InquiryTable("MixedBenchAuthor")]
public sealed class MixedBenchAuthor
{
    [InquiryKey] public int Id { get; set; }
    [InquiryColumn(Length = 100)] public string Name { get; set; } = string.Empty;
}

[InquiryTable("MixedBenchTag")]
public sealed class MixedBenchTag
{
    [InquiryKey] public int Id { get; set; }
    [InquiryColumn] public int PostId { get; set; }
    [InquiryColumn(Length = 100)] public string Label { get; set; } = string.Empty;
}

[InquiryTable("MixedBenchPost")]
public sealed class MixedBenchPost
{
    [InquiryKey] public int Id { get; set; }
    [InquiryColumn] public int AuthorId { get; set; }
    [InquiryColumn(Length = 200)] public string Title { get; set; } = string.Empty;

    [InquiryRelation(nameof(AuthorId))]
    public MixedBenchAuthor? Author { get; set; }

    [InquiryRelation(nameof(MixedBenchTag.PostId))]
    public List<MixedBenchTag>? Tags { get; set; }
}

public partial class MixedBenchPostStore : InquiryStore<MixedBenchPost>
{
    [InquirySelectAllEager]
    public partial IAsyncEnumerable<MixedBenchPost> SelectAllWithAuthorAndTagsAsync(CancellationToken cancellationToken = default);
}
