using Inquiry.Entities;
using System;
using System.Linq;

namespace Inquiry.FeatureCatalog;

[InquiryTable("PrimitiveParent")]
public sealed class PrimitiveParent
{
    [InquiryKey]
    public long Id { get; set; }
}

[InquiryTable("PrimitiveOptionalParent")]
public sealed class PrimitiveOptionalParent
{
    [InquiryKey]
    public long Id { get; set; }
}

[InquiryTable("PrimitiveChild")]
[InquiryIndex(nameof(ParentId), nameof(Code), Name = "IX_PrimitiveChild_Parent_Code")]
[InquiryIndex(nameof(TenantId), nameof(Code), Name = "UX_PrimitiveChild_Tenant_Code", IsUnique = true)]
[InquiryCheck("quantity >= 0", Name = "CK_PrimitiveChild_Quantity")]
[InquiryCheck("code <> ''", Name = "CK_PrimitiveChild_Code")]
public sealed class PrimitiveChild
{
    [InquiryKey]
    public long Id { get; set; }

    [InquiryForeignKey("ParentId", "PrimitiveParent", "Id", ConstraintName = "FK_PrimitiveChild_Parent", OnDelete = InquiryReferentialAction.Cascade)]
    public long ParentId { get; set; }

    [InquiryForeignKey("OptionalParentId", "PrimitiveOptionalParent", "Id", ConstraintName = "FK_PrimitiveChild_OptionalParent", OnDelete = InquiryReferentialAction.SetNull)]
    public long? OptionalParentId { get; set; }

    [InquiryColumn]
    public int TenantId { get; set; }

    [InquiryColumn("code", Length = 32)]
    public string Code { get; set; } = string.Empty;

    [InquiryColumn("quantity")]
    public int Quantity { get; set; }
}

public static class GeneratedSchemaDdl
{
    public static string Extract(string generatedDdl, params string[] tableNames)
        => string.Join("\n\n", generatedDdl
            .Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Where(statement => tableNames.Any(table => statement.Contains(table, StringComparison.Ordinal))));
}
