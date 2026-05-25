namespace Inquiry.Stores;

/// <summary>
/// Generates a method that selects rows by one or more mapped properties or columns.
/// Multiple fields are combined with AND in the WHERE clause; method parameters must match
/// the listed field order and types.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class InquirySelectAllByFieldAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InquirySelectAllByFieldAttribute"/> class.
    /// </summary>
    /// <param name="fields">One or more mapped property or column names. At least one must be supplied.</param>
    public InquirySelectAllByFieldAttribute(params string[] fields)
    {
        if (fields is null || fields.Length == 0)
        {
            throw new ArgumentException("At least one field must be supplied.", nameof(fields));
        }

        for (var i = 0; i < fields.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(fields[i]))
            {
                throw new ArgumentException("Field names cannot be empty.", nameof(fields));
            }
        }

        Fields = fields;
    }

    /// <summary>
    /// Gets the mapped property or column names used in the generated WHERE clause, in declaration order.
    /// </summary>
    public IReadOnlyList<string> Fields { get; }
}
