namespace Inquiry.Stores;

/// <summary>
/// Generates a method that selects rows by one mapped property or column.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class InquirySelectAllByFieldAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InquirySelectAllByFieldAttribute"/> class.
    /// </summary>
    public InquirySelectAllByFieldAttribute(string field)
    {
        if (string.IsNullOrWhiteSpace(field))
        {
            throw new ArgumentException("Field cannot be empty.", nameof(field));
        }

        Field = field;
    }

    /// <summary>
    /// Gets the mapped property or column name used in the generated WHERE clause.
    /// </summary>
    public string Field { get; }
}
