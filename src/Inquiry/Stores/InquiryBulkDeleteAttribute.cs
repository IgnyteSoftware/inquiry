namespace Inquiry.Stores;

/// <summary>
/// Marks an abstract store method as a bulk delete-by-key operation.
/// The method must accept <c>IEnumerable&lt;TKey&gt;</c> and return <c>Task&lt;int&gt;</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class InquiryBulkDeleteAttribute : Attribute
{
}
