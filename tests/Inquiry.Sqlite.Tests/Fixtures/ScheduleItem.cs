using Inquiry.Entities;

namespace Inquiry.Sqlite.Tests.Fixtures;

[InquiryTable("TScheduleItem")]
public sealed class ScheduleItem
{
    [InquiryKey(IsGenerated = true)]
    public long Id { get; set; }

    [InquiryColumn]
    public DateOnly EventDate { get; set; }

    [InquiryColumn]
    public TimeOnly StartTime { get; set; }

    [InquiryColumn]
    public DateOnly? EndDate { get; set; }

    [InquiryColumn]
    public TimeOnly? EndTime { get; set; }
}
