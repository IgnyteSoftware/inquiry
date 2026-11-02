namespace Inquiry.Stores;

/// <summary>
/// Marks an abstract store method as an eager select-all operation. Every navigation property decorated
/// with <c>[InquiryRelation]</c> on the entity is populated in a single round trip: the parent SELECT and
/// each relation's SELECT are batched into one multi-result command. Add <see cref="InquiryIncludeAttribute"/>
/// to load only the named relations. The method must return <c>IAsyncEnumerable&lt;TEntity&gt;</c> or
/// <c>Task&lt;IReadOnlyList&lt;TEntity&gt;&gt;</c>.
/// </summary>
/// <remarks>
/// The relation result sets are read first so each parent can be materialized and stitched one at a time.
/// The <c>IAsyncEnumerable&lt;TEntity&gt;</c> form yields each parent as it is read. The
/// <c>Task&lt;IReadOnlyList&lt;TEntity&gt;&gt;</c> form runs the same command, collects every parent, and
/// closes the reader before the task completes. Two consequences worth knowing:
/// <list type="bullet">
///   <item>
///     In the streaming form, the reader (and, inside a transaction, its connection) stays open for as
///     long as the caller is enumerating. Declare the list form if the consumer is slow or may abandon the loop.
///   </item>
///   <item>
///     The statements in the batch are not read-consistent with each other outside a snapshot or
///     repeatable-read transaction. Because the relation SELECTs run before the parent SELECT, a
///     concurrent commit in between shows up in one direction only: a newly inserted parent arrives with
///     an empty collection, and a deleted child row is still attached to its parent. The latter matters
///     if you branch on a collection's contents — an authorization check can observe a row that was
///     already revoked. Wrap the call in a snapshot/repeatable-read transaction when the parent and its
///     children must be read as of one instant.
///   </item>
/// </list>
/// </remarks>
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
