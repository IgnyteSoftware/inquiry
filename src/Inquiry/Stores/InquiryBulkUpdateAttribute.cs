namespace Inquiry.Stores;

/// <summary>
/// Marks an abstract store method as a bulk update operation.
/// The method must accept <c>IEnumerable&lt;TEntity&gt;</c> and return <c>Task&lt;int&gt;</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class InquiryBulkUpdateAttribute : Attribute
{
}
