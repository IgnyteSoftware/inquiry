namespace Inquiry.Stores;

/// <summary>
/// Marks an abstract store method as an upsert operation (insert if not exists, update if exists).
/// The generated implementation uses the provider-specific upsert syntax. The method returns either
/// the affected-row count or the entity; the return type selects the generated command shape.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class InquiryUpsertAttribute : Attribute
{
}
