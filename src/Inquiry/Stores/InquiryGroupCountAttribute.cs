namespace Inquiry.Stores;

/// <summary>
/// Generates a method that returns the count of rows grouped by the specified column
/// (<c>SELECT col, COUNT(*) FROM t GROUP BY col</c>). The method must return
/// <c>Task&lt;IReadOnlyList&lt;GroupCount&lt;TKey&gt;&gt;&gt;</c> where <c>TKey</c> matches the grouped
/// column's .NET type. Respects the soft-delete active filter when the entity declares one.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class InquiryGroupCountAttribute : Attribute
{
    /// <summary>Initializes a new instance with the column to group by.</summary>
    /// <param name="column">The mapped property or column name to GROUP BY.</param>
    public InquiryGroupCountAttribute(string column)
    {
        if (string.IsNullOrWhiteSpace(column))
        {
            throw new ArgumentException("Column cannot be empty.", nameof(column));
        }

        Column = column;
    }

    /// <summary>Gets the mapped property or column name to GROUP BY.</summary>
    public string Column { get; }

    /// <inheritdoc cref="InquirySelectAllAttribute.IncludeDeleted"/>
    public bool IncludeDeleted { get; set; }
}
