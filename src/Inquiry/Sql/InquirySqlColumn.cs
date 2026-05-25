namespace Inquiry.Sql;

/// <summary>
/// Describes a mapped column used to build Inquiry generated SQL statements.
/// </summary>
public sealed class InquirySqlColumn
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InquirySqlColumn"/> class.
    /// </summary>
    public InquirySqlColumn(
        string propertyName,
        string columnName,
        bool isKey,
        bool isGenerated = false,
        bool useDatabaseDefault = false)
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
        IsGenerated = isGenerated;
        UseDatabaseDefault = useDatabaseDefault;
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

    /// <summary>
    /// Gets a value indicating whether the database supplies this column's value
    /// (for example, IDENTITY or AUTOINCREMENT keys). Generated columns are excluded
    /// from INSERT and UPDATE statements.
    /// </summary>
    public bool IsGenerated { get; }

    /// <summary>
    /// Gets a value indicating whether INSERT statements should omit this column
    /// so the database default expression supplies the value. Unlike generated columns,
    /// defaulted columns remain updateable after insertion.
    /// </summary>
    public bool UseDatabaseDefault { get; }
}
