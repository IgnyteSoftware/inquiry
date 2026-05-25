using Inquiry.Entities;

namespace Inquiry.Northwind.Models;

[InquiryTable("Shippers")]
public sealed class Shipper
{
    [InquiryKey("ShipperID", IsGenerated = true)]
    public int? ShipperID { get; set; }

    [InquiryColumn]
    public string CompanyName { get; set; } = string.Empty;

    [InquiryColumn]
    public string? Phone { get; set; }
}
