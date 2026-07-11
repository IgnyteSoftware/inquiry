using Inquiry.Entities;

namespace Inquiry.SqlServer.Tests.Fixtures;

[InquiryTable("TAnsiPlanCacheItem")]
public sealed class AnsiPlanCacheItem
{
    [InquiryKey("Id", UseDatabaseDefault = true)] public int? Id { get; set; }
    [InquiryColumn(Length = 64, IsUnicode = false)] public string Code { get; set; } = string.Empty;
}
