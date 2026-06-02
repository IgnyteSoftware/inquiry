using Inquiry.Entities;

namespace Inquiry.Northwind.Models;

[InquiryTable("Employees")]
public sealed class Employee
{
    [InquiryKey("EmployeeID", IsGenerated = true)]
    public int? EmployeeID { get; set; }

    [InquiryColumn(IsIndexed = true, Length = 40)]
    public string LastName { get; set; } = string.Empty;

    [InquiryColumn]
    public string FirstName { get; set; } = string.Empty;

    [InquiryColumn]
    public string? Title { get; set; }

    [InquiryColumn]
    public string? TitleOfCourtesy { get; set; }

    [InquiryColumn]
    public DateTime? BirthDate { get; set; }

    [InquiryColumn]
    public DateTime? HireDate { get; set; }

    [InquiryColumn]
    public string? Address { get; set; }

    [InquiryColumn]
    public string? City { get; set; }

    [InquiryColumn]
    public string? Region { get; set; }

    [InquiryColumn(IsIndexed = true, Length = 20)]
    public string? PostalCode { get; set; }

    [InquiryColumn]
    public string? Country { get; set; }

    [InquiryColumn]
    public string? HomePhone { get; set; }

    [InquiryColumn]
    public string? Extension { get; set; }

    [InquiryColumn]
    public string? Notes { get; set; }

    [InquiryForeignKey("ReportsTo", "Employees", "EmployeeID")]
    public int? ReportsTo { get; set; }

    [InquiryColumn]
    public string? PhotoPath { get; set; }
}
