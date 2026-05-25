using Inquiry.Entities;

namespace Inquiry.Northwind.Models;

[InquiryTable("CustomerCustomerDemo")]
public sealed class CustomerCustomerDemo
{
    [InquiryKey("CustomerID")]
    public string CustomerID { get; set; } = string.Empty;

    [InquiryKey("CustomerTypeID")]
    public string CustomerTypeID { get; set; } = string.Empty;
}
