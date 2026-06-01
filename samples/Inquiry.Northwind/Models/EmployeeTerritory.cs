using Inquiry.Entities;

namespace Inquiry.Northwind.Models;

[InquiryTable("EmployeeTerritories")]
public sealed class EmployeeTerritory
{
    [InquiryKey("EmployeeID")]
    public int EmployeeID { get; set; }

    [InquiryKey("TerritoryID", Length = 40)]
    public string TerritoryID { get; set; } = string.Empty;
}
