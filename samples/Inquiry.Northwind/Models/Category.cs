using Inquiry.Entities;

namespace Inquiry.Northwind.Models;

[InquiryTable("Categories")]
public sealed class Category
{
    [InquiryKey("CategoryID", IsGenerated = true)]
    public int? CategoryID { get; set; }

    [InquiryColumn(IsIndexed = true, Length = 40)]
    public string CategoryName { get; set; } = string.Empty;

    [InquiryColumn]
    public string? Description { get; set; }

    [InquiryRelation(nameof(Product.CategoryID))]
    public List<Product>? Products { get; set; }
}
