namespace Inquiry.Stores;

/// <summary>
/// Marks an abstract store method as an eager select-all operation.
/// All navigation properties decorated with <c>[InquiryRelation]</c> on the entity are populated via N+1 loading.
/// The method must return <c>IAsyncEnumerable&lt;TEntity&gt;</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class InquirySelectAllEagerAttribute : Attribute
{
    /// <summary>
    /// Gets or sets a value indicating whether the parent select includes soft-deleted rows. Has an
    /// effect only when the entity declares an <c>[InquirySoftDelete]</c> column. Note this controls
    /// only the parent query; eager-loaded children are always filtered by their own soft-delete column.
    /// </summary>
    public bool IncludeDeleted { get; set; }
}
