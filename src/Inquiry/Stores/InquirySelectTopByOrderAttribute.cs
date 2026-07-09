namespace Inquiry.Stores;

/// <summary>
/// Generates a method that returns the single row with the extreme value of the specified column
/// (<c>SELECT … ORDER BY col [ASC|DESC] LIMIT 1</c>). The method must return <c>Task&lt;T?&gt;</c>
/// where <c>T</c> is the store entity. Returns <see langword="null"/> when the table is empty.
/// Respects the soft-delete active filter when the entity declares one.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class InquirySelectTopByOrderAttribute : Attribute
{
    /// <summary>Initializes a new instance with the column to order by.</summary>
    /// <param name="column">The mapped property or column name to ORDER BY.</param>
    public InquirySelectTopByOrderAttribute(string column)
    {
        if (string.IsNullOrWhiteSpace(column))
        {
            throw new ArgumentException("Column cannot be empty.", nameof(column));
        }

        Column = column;
    }

    /// <summary>Gets the mapped property or column name to ORDER BY.</summary>
    public string Column { get; }

    /// <summary>
    /// Gets or sets a value indicating whether to order descending (largest/latest first).
    /// Defaults to <see langword="false"/> (ascending — smallest/earliest first).
    /// </summary>
    public bool Descending { get; set; }

    /// <inheritdoc cref="InquirySelectAllAttribute.IncludeDeleted"/>
    public bool IncludeDeleted { get; set; }
}
