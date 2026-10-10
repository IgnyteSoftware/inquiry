using System.ComponentModel.DataAnnotations;
using Inquiry.Entities;

[assembly: Inquiry.InquiryDialect("Sqlite")]

namespace Inquiry.Sample.Data;

[InquiryTable("ShowcaseProducts")]
public sealed class WorkspaceProduct
{
    [InquiryKey(IsGenerated = true)]
    public int Id { get; set; }

    [InquiryColumn(Length = 100)]
    [Required, StringLength(100)]
    public string Name { get; set; } = "";

    [InquiryColumn(Precision = 12, Scale = 2)]
    [Range(typeof(decimal), "0", "999999.99")]
    public decimal Price { get; set; }

    [InquiryColumn]
    [Range(0, 1000000)]
    public int Stock { get; set; }

    [InquiryForeignKey(typeof(WorkspaceCategory))]
    public int? CategoryId { get; set; }

    [InquiryColumn(Length = 500)]
    [StringLength(500)]
    public string? Description { get; set; }

    [InquiryColumn, InquiryGlobalFilter(ContextKey = "WorkspaceId", EnforceOnWrites = true)]
    public int WorkspaceId { get; set; }

    [InquiryConcurrencyToken]
    public int Version { get; set; }

    [InquiryColumn, InquirySoftDelete]
    public bool IsDeleted { get; set; }
}

[InquiryTable("ShowcaseCategories")]
public sealed class WorkspaceCategory
{
    [InquiryKey]
    public int Id { get; set; }

    [InquiryColumn(Length = 50)]
    public string Name { get; set; } = "";
}

[InquiryAdHoc]
public sealed class InventoryTotals
{
    public long Products { get; set; }
    public long Units { get; set; }
    public decimal Value { get; set; }
}
