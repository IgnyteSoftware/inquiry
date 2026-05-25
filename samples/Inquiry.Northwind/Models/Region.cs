using Inquiry.Entities;

namespace Inquiry.Northwind.Models;

[InquiryTable("Region")]
public sealed class Region
{
    [InquiryKey("RegionID")]
    public int RegionID { get; set; }

    [InquiryColumn]
    public string RegionDescription { get; set; } = string.Empty;

    [InquiryRelation(nameof(Territory.RegionID))]
    public List<Territory>? Territories { get; set; }
}
