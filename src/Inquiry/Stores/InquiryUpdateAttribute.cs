namespace Inquiry.Stores;

/// <summary>
/// Generates an update method. Without <see cref="InquiryWhereAttribute"/> criteria, the method
/// accepts an entity and updates it by primary key, or accepts an
/// <see cref="System.Collections.Generic.IEnumerable{T}"/> of entities and updates each row by its key.
/// With one or more criteria, the method performs a partial update: leading scalar parameters map by
/// name to SET columns and trailing parameters bind the criteria positionally.
/// <see cref="InquirySetAttribute"/> can replace the inferred SET columns with compile-time expressions.
/// A single-entity method returns either <see cref="bool"/> or the updated entity. The return type
/// selects the generated command shape.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class InquiryUpdateAttribute : Attribute
{
    /// <summary>
    /// Gets or sets a value indicating whether a set-based update (one with
    /// <see cref="InquiryWhereAttribute"/> criteria) also writes the entity's
    /// <see cref="Inquiry.Entities.InquiryModifiedAtAttribute"/> and
    /// <see cref="Inquiry.Entities.InquiryModifiedByAttribute"/> columns. The generated SQL sets them to
    /// <c>UtcNow</c> and <see cref="Inquiry.InquiryAuditContext.CurrentUser"/>, as an entity update does,
    /// so the method takes no parameters for them. The default is <see langword="false"/>, which leaves
    /// the generated SQL unchanged. Entity updates always stamp these columns and ignore this property.
    /// </summary>
    public bool StampModified { get; set; }
}
