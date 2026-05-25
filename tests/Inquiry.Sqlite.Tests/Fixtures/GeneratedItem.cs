using Inquiry.Entities;

namespace Inquiry.Sqlite.Tests.Fixtures;

[InquiryTable("TGeneratedItem")]
public sealed class GeneratedItem
{
    [InquiryKey("Id", IsGenerated = true)]
    public int? Id { get; set; }

    [InquiryColumn]
    public string Name { get; set; } = string.Empty;
}
