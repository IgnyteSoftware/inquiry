using Inquiry.Entities;

namespace Inquiry.SqlServer.Tests.Fixtures;

// A declared-length string column (Length = 64) so the generated SQL Server binder emits
// `_p.Size = 64`. Used to prove the sp_executesql parameter signature stays stable across value
// lengths (#56), which keeps one cached plan instead of one per distinct value length.
[InquiryTable("TPlanCacheItem")]
public sealed class PlanCacheItem
{
    [InquiryKey("Id", UseDatabaseDefault = true)] public int? Id { get; set; }
    [InquiryColumn(Length = 64)] public string Name { get; set; } = string.Empty;
}
