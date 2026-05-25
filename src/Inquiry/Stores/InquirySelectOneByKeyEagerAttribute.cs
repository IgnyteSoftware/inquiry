namespace Inquiry.Stores;

/// <summary>
/// Marks an abstract store method as an eager single-entity lookup by key.
/// All navigation properties decorated with <c>[InquiryRelation]</c> on the entity are populated.
/// The method must accept the key type and return <c>Task&lt;TEntity?&gt;</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class InquirySelectOneByKeyEagerAttribute : Attribute
{
}
