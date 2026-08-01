namespace Inquiry.Stores;

/// <summary>
/// Marks an abstract store method as an eager single-entity lookup by key.
/// All navigation properties decorated with <c>[InquiryRelation]</c> on the entity are populated.
/// The method must accept the key type and return <c>Task&lt;TEntity?&gt;</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class InquirySelectOneByKeyEagerAttribute : Attribute
{
    /// <summary>
    /// Gets or sets a value indicating whether the parent lookup includes a soft-deleted row. Has an
    /// effect only when the entity declares an <c>[InquirySoftDelete]</c> column. Note this controls
    /// only the parent query; eager-loaded children are always filtered by their own soft-delete column.
    /// </summary>
    public bool IncludeDeleted { get; set; }
}
