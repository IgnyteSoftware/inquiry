using Inquiry.Entities;

namespace Inquiry.Northwind.Models;

[InquiryTable("EmployeeTerritories")]
public sealed class EmployeeTerritory
{
    [InquiryKey("EmployeeID")]
    [InquiryForeignKey("EmployeeID", "Employees", "EmployeeID")]
    public int EmployeeID { get; set; }

    [InquiryKey("TerritoryID", Length = 40)]
    [InquiryForeignKey("TerritoryID", "Territories", "TerritoryID")]
    public string TerritoryID { get; set; } = string.Empty;
}
