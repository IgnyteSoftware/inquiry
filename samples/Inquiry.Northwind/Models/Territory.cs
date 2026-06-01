using Inquiry.Entities;

namespace Inquiry.Northwind.Models;

[InquiryTable("Territories")]
public sealed class Territory
{
    [InquiryKey("TerritoryID", Length = 40)]
    public string TerritoryID { get; set; } = string.Empty;

    [InquiryColumn]
    public string TerritoryDescription { get; set; } = string.Empty;

    [InquiryForeignKey("RegionID", "Region", "RegionID")]
    public int RegionID { get; set; }

    [InquiryRelation(nameof(RegionID))]
    public Region? Region { get; set; }
}
