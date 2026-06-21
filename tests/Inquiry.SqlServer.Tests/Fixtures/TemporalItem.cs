using System;
using Inquiry.Entities;

namespace Inquiry.SqlServer.Tests.Fixtures;

// A datetime2-backed fixture for exercising DateTime IN-list binding (#52). The Northwind Orders
// columns are legacy `datetime`, which rounds at storage time and cannot distinguish the bug.
[InquiryTable("TemporalItem")]
public sealed class TemporalItem
{
    [InquiryKey("Id", IsGenerated = true)] public int? Id { get; set; }
    [InquiryColumn] public DateTime Moment { get; set; }
}
