using Inquiry.Entities;

namespace Inquiry.Northwind.Models;

[InquiryTable("Customers")]
public sealed class Customer
{
    [InquiryKey("CustomerID", Length = 5)]
    public string CustomerID { get; set; } = string.Empty;

    [InquiryColumn(IsIndexed = true, Length = 40)]
    public string CompanyName { get; set; } = string.Empty;

    [InquiryColumn]
    public string? ContactName { get; set; }

    [InquiryColumn]
    public string? ContactTitle { get; set; }

    [InquiryColumn]
    public string? Address { get; set; }

    [InquiryColumn(IsIndexed = true, Length = 60)]
    public string? City { get; set; }

    [InquiryColumn(IsIndexed = true, Length = 60)]
    public string? Region { get; set; }

    [InquiryColumn(IsIndexed = true, Length = 20)]
    public string? PostalCode { get; set; }

    [InquiryColumn]
    public string? Country { get; set; }

    [InquiryColumn]
    public string? Phone { get; set; }

    [InquiryColumn]
    public string? Fax { get; set; }
}
