namespace Inquiry.Stores;

/// <summary>
/// Generates a method that selects a single row by primary key.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class InquirySelectOneByKeyAttribute : Attribute
{
    /// <summary>
    /// Gets or sets a value indicating whether the generated query includes soft-deleted rows. Has an
    /// effect only when the entity declares an <c>[InquirySoftDelete]</c> column: when false (the
    /// default) the query auto-appends the active filter (<c>= 0</c> / <c>IS NULL</c>); when true the
    /// query is emitted unfiltered so a soft-deleted row can be returned.
    /// </summary>
    public bool IncludeDeleted { get; set; }
}
