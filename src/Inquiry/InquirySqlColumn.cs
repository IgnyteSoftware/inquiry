namespace Inquiry;

/// <summary>
/// Describes a mapped column used to build Inquiry generated SQL statements.
/// </summary>
public sealed class InquirySqlColumn
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InquirySqlColumn"/> class.
    /// </summary>
    public InquirySqlColumn(string propertyName, string columnName, bool isKey)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            throw new ArgumentException("Property name cannot be empty.", nameof(propertyName));
        }

        if (string.IsNullOrWhiteSpace(columnName))
        {
            throw new ArgumentException("Column name cannot be empty.", nameof(columnName));
        }

        PropertyName = propertyName;
        ColumnName = columnName;
        IsKey = isKey;
    }

    /// <summary>
    /// Gets the mapped entity property name.
    /// </summary>
    public string PropertyName { get; }

    /// <summary>
    /// Gets the database column name.
    /// </summary>
    public string ColumnName { get; }

    /// <summary>
    /// Gets a value indicating whether this column is the entity key.
    /// </summary>
    public bool IsKey { get; }
}
