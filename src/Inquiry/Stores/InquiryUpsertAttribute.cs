namespace Inquiry.Stores;

/// <summary>
/// Marks an abstract store method as an upsert operation (insert if not exists, update if exists).
/// The generated implementation uses the provider-specific upsert syntax. The method returns either
/// the affected-row count or the entity; the return type selects the generated command shape.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class InquiryUpsertAttribute : Attribute
{
    /// <summary>
    /// Gets or sets the mapped properties that identify an existing row, instead of the entity key.
    /// Empty (the default) matches on the key.
    /// </summary>
    /// <remarks>
    /// The entity must declare a unique index on exactly these properties, with
    /// <c>[InquiryIndex(..., IsUnique = true)]</c> or, for one property,
    /// <c>[InquiryColumn(IsUnique = true)]</c>; otherwise the build reports INQ121. The update branch
    /// does not change the key, so the key of an existing row stays as it is.
    /// </remarks>
    public string[] MatchOn { get; set; } = [];
}
