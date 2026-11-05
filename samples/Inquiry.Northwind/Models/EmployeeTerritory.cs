using Inquiry.Entities;

namespace Inquiry.Northwind.Models;

[InquiryTable("EmployeeTerritories")]
public sealed class EmployeeTerritory
{
    [InquiryKey("EmployeeID")]
    [InquiryForeignKey(typeof(Employee))]
    public int EmployeeID { get; set; }

    [InquiryKey("TerritoryID", Length = 40)]
    [InquiryForeignKey(typeof(Territory))]
    public string TerritoryID { get; set; } = string.Empty;
}
