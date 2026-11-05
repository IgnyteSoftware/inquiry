using Inquiry.Entities;

namespace Fulfillment;

[InquiryTable("WorkItem")]
public sealed class WorkItem
{
    [InquiryKey(IsGenerated = true)]
    public long Id { get; set; }

    [InquiryColumn]
    public int QueueId { get; set; }

    [InquiryColumn]
    public int State { get; set; }

    [InquiryColumn]
    public int Attempts { get; set; }
}
