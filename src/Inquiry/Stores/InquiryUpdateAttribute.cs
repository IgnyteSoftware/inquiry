namespace Inquiry.Stores;

/// <summary>
/// Generates an update method. Without <see cref="InquiryWhereAttribute"/> criteria, the method
/// accepts an entity and updates it by primary key, or accepts an
/// <see cref="System.Collections.Generic.IEnumerable{T}"/> of entities and updates each row by its key.
/// With one or more criteria, the method performs a partial update: leading scalar parameters map by
/// name to SET columns and trailing parameters bind the criteria positionally.
/// <see cref="InquirySetAttribute"/> can replace the inferred SET columns with compile-time expressions.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class InquiryUpdateAttribute : Attribute
{
    /// <summary>
    /// Gets or sets a value indicating whether the generated method returns the row produced by the database.
    /// This option is not supported on batch updates or partial updates that use
    /// <see cref="InquiryWhereAttribute"/>.
    /// </summary>
    public bool ReturnEntity { get; set; }
}
