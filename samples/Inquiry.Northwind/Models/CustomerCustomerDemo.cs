using Inquiry.Entities;

namespace Inquiry.Northwind.Models;

[InquiryTable("CustomerCustomerDemo")]
public sealed class CustomerCustomerDemo
{
    [InquiryKey("CustomerID", Length = 5)]
    [InquiryForeignKey("CustomerID", "Customers", "CustomerID")]
    public string CustomerID { get; set; } = string.Empty;

    [InquiryKey("CustomerTypeID", Length = 10)]
    [InquiryForeignKey("CustomerTypeID", "CustomerDemographics", "CustomerTypeID")]
    public string CustomerTypeID { get; set; } = string.Empty;
}
