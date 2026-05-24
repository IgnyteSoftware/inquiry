namespace Inquiry.Entities;

/// <summary>
/// Describes a mapped foreign-key relationship for an Inquiry entity.
/// </summary>
public sealed class InquiryForeignKey
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InquiryForeignKey"/> class.
    /// </summary>
    public InquiryForeignKey(string propertyName, string columnName, string referencedTable, string referencedColumn)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            throw new ArgumentException("Property name cannot be empty.", nameof(propertyName));
        }

        if (string.IsNullOrWhiteSpace(columnName))
        {
            throw new ArgumentException("Column name cannot be empty.", nameof(columnName));
        }

        if (string.IsNullOrWhiteSpace(referencedTable))
        {
            throw new ArgumentException("Referenced table cannot be empty.", nameof(referencedTable));
        }

        if (string.IsNullOrWhiteSpace(referencedColumn))
        {
            throw new ArgumentException("Referenced column cannot be empty.", nameof(referencedColumn));
        }

        PropertyName = propertyName;
        ColumnName = columnName;
        ReferencedTable = referencedTable;
        ReferencedColumn = referencedColumn;
    }

    /// <summary>
    /// Gets the mapped entity property name containing the foreign-key value.
    /// </summary>
    public string PropertyName { get; }

    /// <summary>
    /// Gets the mapped database column name containing the foreign-key value.
    /// </summary>
    public string ColumnName { get; }

    /// <summary>
    /// Gets the referenced table name.
    /// </summary>
    public string ReferencedTable { get; }

    /// <summary>
    /// Gets the referenced column name.
    /// </summary>
    public string ReferencedColumn { get; }
}
