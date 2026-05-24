namespace Inquiry.Entities;

/// <summary>
/// Maps a CLR property to a foreign-key column and records relationship metadata.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class InquiryForeignKeyAttribute : InquiryColumnAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InquiryForeignKeyAttribute"/> class.
    /// </summary>
    public InquiryForeignKeyAttribute(string referencedTable, string referencedColumn)
    {
        if (string.IsNullOrWhiteSpace(referencedTable))
        {
            throw new ArgumentException("Referenced table cannot be empty.", nameof(referencedTable));
        }

        if (string.IsNullOrWhiteSpace(referencedColumn))
        {
            throw new ArgumentException("Referenced column cannot be empty.", nameof(referencedColumn));
        }

        ReferencedTable = referencedTable;
        ReferencedColumn = referencedColumn;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InquiryForeignKeyAttribute"/> class.
    /// </summary>
    public InquiryForeignKeyAttribute(string columnName, string referencedTable, string referencedColumn)
        : base(columnName)
    {
        if (string.IsNullOrWhiteSpace(referencedTable))
        {
            throw new ArgumentException("Referenced table cannot be empty.", nameof(referencedTable));
        }

        if (string.IsNullOrWhiteSpace(referencedColumn))
        {
            throw new ArgumentException("Referenced column cannot be empty.", nameof(referencedColumn));
        }

        ReferencedTable = referencedTable;
        ReferencedColumn = referencedColumn;
    }

    /// <summary>
    /// Gets the referenced table name.
    /// </summary>
    public string ReferencedTable { get; }

    /// <summary>
    /// Gets the referenced column name.
    /// </summary>
    public string ReferencedColumn { get; }
}
