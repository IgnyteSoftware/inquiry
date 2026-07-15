using Inquiry.Entities;

namespace Inquiry.UpdateActionCatalog;

[InquiryTable("UpdateCascadeParent")]
public sealed class UpdateCascadeParent
{
    [InquiryKey]
    public long Id { get; set; }
}

[InquiryTable("UpdateCascadeChild")]
public sealed class UpdateCascadeChild
{
    [InquiryKey]
    public long Id { get; set; }

    [InquiryForeignKey("ParentId", "UpdateCascadeParent", "Id", ConstraintName = "FK_UpdateCascade_Parent", OnUpdate = InquiryReferentialAction.Cascade)]
    public long ParentId { get; set; }
}
