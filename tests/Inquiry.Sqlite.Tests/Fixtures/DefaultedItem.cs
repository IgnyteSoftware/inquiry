using Inquiry.Entities;

namespace Inquiry.Sqlite.Tests.Fixtures;

[InquiryTable("TDefaultedItem")]
public sealed class DefaultedItem
{
    [InquiryKey]
    public Guid Key { get; set; } = Guid.NewGuid();

    [InquiryColumn]
    public string Name { get; set; } = string.Empty;

    [InquiryColumn(UseDatabaseDefault = true)]
    public string Status { get; set; } = string.Empty;
}
