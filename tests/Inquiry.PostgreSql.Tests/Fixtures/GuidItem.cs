using System;
using Inquiry.Entities;

namespace Inquiry.PostgreSql.Tests.Fixtures;

[InquiryTable("TGuidItem")]
public sealed class GuidItem
{
    [InquiryKey("Id", UseDatabaseDefault = true)] public Guid? Id { get; set; }
    [InquiryColumn] public string Name { get; set; } = string.Empty;
}
