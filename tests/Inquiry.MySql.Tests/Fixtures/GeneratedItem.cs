using Inquiry.Entities;

namespace Inquiry.MySql.Tests.Fixtures;

[InquiryTable("TGeneratedItem")]
public sealed class GeneratedItem
{
    [InquiryKey("Id", IsGenerated = true)]
    public long? Id { get; set; }

    [InquiryColumn]
    public string Name { get; set; } = string.Empty;
}
