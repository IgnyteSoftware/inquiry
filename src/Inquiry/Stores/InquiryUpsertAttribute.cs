namespace Inquiry.Stores;

/// <summary>
/// Marks an abstract store method as an upsert operation (insert if not exists, update if exists).
/// The generated implementation uses the provider-specific upsert syntax.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class InquiryUpsertAttribute : Attribute
{
}
