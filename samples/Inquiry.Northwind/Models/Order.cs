using Inquiry.Entities;

namespace Inquiry.Northwind.Models;

[InquiryTable("Orders")]
public sealed class Order
{
    [InquiryKey("OrderID", IsGenerated = true)]
    public int? OrderID { get; set; }

    [InquiryForeignKey("CustomerID", "Customers", "CustomerID")]
    public string? CustomerID { get; set; }

    [InquiryForeignKey("EmployeeID", "Employees", "EmployeeID")]
    public int? EmployeeID { get; set; }

    [InquiryColumn]
    public DateTime? OrderDate { get; set; }

    [InquiryColumn]
    public DateTime? RequiredDate { get; set; }

    [InquiryColumn]
    public DateTime? ShippedDate { get; set; }

    [InquiryForeignKey("ShipVia", "Shippers", "ShipperID")]
    public int? ShipVia { get; set; }

    [InquiryColumn]
    public decimal? Freight { get; set; }

    [InquiryColumn]
    public string? ShipName { get; set; }

    [InquiryColumn]
    public string? ShipAddress { get; set; }

    [InquiryColumn]
    public string? ShipCity { get; set; }

    [InquiryColumn]
    public string? ShipRegion { get; set; }

    [InquiryColumn]
    public string? ShipPostalCode { get; set; }

    [InquiryColumn]
    public string? ShipCountry { get; set; }
}
