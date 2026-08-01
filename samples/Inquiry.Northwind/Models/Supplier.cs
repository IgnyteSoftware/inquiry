using Inquiry.Entities;

namespace Inquiry.Northwind.Models;

[InquiryTable("Suppliers")]
public sealed class Supplier
{
    [InquiryKey("SupplierID", IsGenerated = true)]
    public int? SupplierID { get; set; }

    [InquiryColumn(IsIndexed = true, Length = 40)]
    public string CompanyName { get; set; } = string.Empty;

    [InquiryColumn]
    public string? ContactName { get; set; }

    [InquiryColumn]
    public string? ContactTitle { get; set; }

    [InquiryColumn]
    public string? Address { get; set; }

    [InquiryColumn]
    public string? City { get; set; }

    [InquiryColumn]
    public string? Region { get; set; }

    [InquiryColumn(IsIndexed = true, Length = 20)]
    public string? PostalCode { get; set; }

    [InquiryColumn]
    public string? Country { get; set; }

    [InquiryColumn]
    public string? Phone { get; set; }

    [InquiryColumn]
    public string? Fax { get; set; }

    [InquiryColumn]
    public string? HomePage { get; set; }
}
