using Inquiry.Entities;

namespace Inquiry.Sample.Models;

[InquiryTable("TProduct")]
public sealed class Product
{
    [InquiryKey]
    public Guid Key { get; set; } = Guid.NewGuid();

    [InquiryColumn]
    public string Name { get; set; } = string.Empty;

    [InquiryColumn]
    public decimal Price { get; set; }

    [InquiryForeignKey("CategoryKey", "TCategory", "Key")]
    public Guid CategoryKey { get; set; }
}
