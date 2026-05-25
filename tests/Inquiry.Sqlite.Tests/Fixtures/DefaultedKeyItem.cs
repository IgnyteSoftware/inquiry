using Inquiry.Entities;

namespace Inquiry.Sqlite.Tests.Fixtures;

[InquiryTable("TDefaultedKeyItem")]
public sealed class DefaultedKeyItem
{
    [InquiryKey(UseDatabaseDefault = true)]
    public string? Id { get; set; }

    [InquiryColumn]
    public string Name { get; set; } = string.Empty;
}
