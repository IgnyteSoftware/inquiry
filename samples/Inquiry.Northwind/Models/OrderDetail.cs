using Inquiry.Entities;

namespace Inquiry.Northwind.Models;

[InquiryTable("Order Details")]
public sealed class OrderDetail
{
    [InquiryKey("OrderID")]
    public int OrderID { get; set; }

    [InquiryKey("ProductID")]
    public int ProductID { get; set; }

    [InquiryColumn]
    public decimal UnitPrice { get; set; }

    [InquiryColumn]
    public short Quantity { get; set; }

    [InquiryColumn]
    public float Discount { get; set; }
}
