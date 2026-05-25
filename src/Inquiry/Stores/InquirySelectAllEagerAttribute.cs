namespace Inquiry.Stores;

/// <summary>
/// Marks an abstract store method as an eager select-all operation.
/// All navigation properties decorated with <c>[InquiryRelation]</c> on the entity are populated via N+1 loading.
/// The method must return <c>IAsyncEnumerable&lt;TEntity&gt;</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class InquirySelectAllEagerAttribute : Attribute
{
}
