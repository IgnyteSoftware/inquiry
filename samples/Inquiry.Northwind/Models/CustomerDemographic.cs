using Inquiry.Entities;

namespace Inquiry.Northwind.Models;

[InquiryTable("CustomerDemographics")]
public sealed class CustomerDemographic
{
    [InquiryKey("CustomerTypeID", Length = 10)]
    public string CustomerTypeID { get; set; } = string.Empty;

    [InquiryColumn]
    public string? CustomerDesc { get; set; }
}
