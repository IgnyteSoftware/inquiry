using Inquiry.Entities;

namespace Inquiry.Northwind.Models;

[InquiryTable("Products")]
public sealed class Product
{
    [InquiryKey("ProductID", IsGenerated = true)]
    public int? ProductID { get; set; }

    [InquiryColumn(IsIndexed = true)]
    public string ProductName { get; set; } = string.Empty;

    [InquiryForeignKey("SupplierID", "Suppliers", "SupplierID", IsIndexed = true)]
    public int? SupplierID { get; set; }

    [InquiryForeignKey("CategoryID", "Categories", "CategoryID", IsIndexed = true)]
    public int? CategoryID { get; set; }

    [InquiryColumn]
    public string? QuantityPerUnit { get; set; }

    [InquiryColumn(Precision = 19, Scale = 4)]
    public decimal? UnitPrice { get; set; }

    [InquiryColumn]
    public short? UnitsInStock { get; set; }

    [InquiryColumn]
    public short? UnitsOnOrder { get; set; }

    [InquiryColumn]
    public short? ReorderLevel { get; set; }

    [InquiryColumn]
    public bool Discontinued { get; set; }

    [InquiryRelation(nameof(CategoryID))]
    public Category? Category { get; set; }
}
