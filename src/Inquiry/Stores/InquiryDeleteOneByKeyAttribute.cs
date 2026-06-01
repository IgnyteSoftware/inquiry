namespace Inquiry.Stores;

/// <summary>
/// Generates a method that deletes an entity by primary key.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class InquiryDeleteOneByKeyAttribute : Attribute
{
    /// <summary>
    /// Gets or sets a value indicating whether the method emits a literal <c>DELETE</c> even when the
    /// entity declares an <c>[InquirySoftDelete]</c> column. When false (the default) a soft-delete
    /// entity is deleted via an UPDATE that sets the indicator; when true the row is physically removed.
    /// Has no effect on entities without a soft-delete column (always a literal <c>DELETE</c>).
    /// </summary>
    public bool HardDelete { get; set; }
}
