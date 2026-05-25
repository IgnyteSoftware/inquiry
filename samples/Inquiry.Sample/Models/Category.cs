using Inquiry.Entities;

namespace Inquiry.Sample.Models;

[InquiryTable("TCategory")]
public sealed class Category
{
    [InquiryKey]
    public Guid Key { get; set; } = Guid.NewGuid();

    [InquiryColumn]
    public string Name { get; set; } = string.Empty;

    [InquiryRelation("CategoryKey")]
    public List<Product>? Products { get; set; }
}
