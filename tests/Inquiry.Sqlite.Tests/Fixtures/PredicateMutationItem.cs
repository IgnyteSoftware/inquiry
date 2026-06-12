using Inquiry.Entities;

namespace Inquiry.Sqlite.Tests.Fixtures;

[InquiryTable("TPredicateMutationItem")]
public sealed class PredicateMutationItem
{
    [InquiryKey(IsGenerated = true)]
    public long Id { get; set; }

    [InquiryColumn]
    public string Category { get; set; } = string.Empty;

    [InquiryColumn]
    public decimal Price { get; set; }
}
