using Inquiry.Entities;

namespace Inquiry.Northwind.Models;

[InquiryTable("Orders")]
public sealed class Order
{
    [InquiryKey("OrderID", IsGenerated = true)]
    public int? OrderID { get; set; }

    [InquiryForeignKey("CustomerID", "Customers", "CustomerID", Length = 5, IsIndexed = true)]
    public string? CustomerID { get; set; }

    [InquiryForeignKey("EmployeeID", "Employees", "EmployeeID", IsIndexed = true)]
    public int? EmployeeID { get; set; }

    [InquiryColumn(IsIndexed = true)]
    public DateTime? OrderDate { get; set; }

    [InquiryColumn]
    public DateTime? RequiredDate { get; set; }

    [InquiryColumn(IsIndexed = true)]
    public DateTime? ShippedDate { get; set; }

    [InquiryForeignKey("ShipVia", "Shippers", "ShipperID", IsIndexed = true)]
    public int? ShipVia { get; set; }

    [InquiryColumn(Precision = 19, Scale = 4)]
    public decimal? Freight { get; set; }

    [InquiryColumn]
    public string? ShipName { get; set; }

    [InquiryColumn]
    public string? ShipAddress { get; set; }

    [InquiryColumn]
    public string? ShipCity { get; set; }

    [InquiryColumn]
    public string? ShipRegion { get; set; }

    [InquiryColumn(IsIndexed = true)]
    public string? ShipPostalCode { get; set; }

    [InquiryColumn]
    public string? ShipCountry { get; set; }
}
