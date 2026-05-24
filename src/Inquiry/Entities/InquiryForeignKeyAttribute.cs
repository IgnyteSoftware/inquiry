namespace Inquiry.Entities;

/// <summary>
/// Maps a CLR property to a foreign-key column and documents the referenced table and column.
/// </summary>
/// <remarks>
/// At runtime this attribute behaves exactly like <see cref="InquiryColumnAttribute"/>; the
/// referenced-table and referenced-column information is preserved for documentation and for
/// callers that introspect entity attributes via reflection. SQL generation treats foreign-key
/// columns the same as any other mapped column.
/// </remarks>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class InquiryForeignKeyAttribute : InquiryColumnAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InquiryForeignKeyAttribute"/> class with the
    /// mapped column name defaulting to the property name.
    /// </summary>
    public InquiryForeignKeyAttribute(string referencedTable, string referencedColumn)
    {
        ValidateReference(referencedTable, referencedColumn);
        ReferencedTable = referencedTable;
        ReferencedColumn = referencedColumn;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InquiryForeignKeyAttribute"/> class with an
    /// explicit local column name.
    /// </summary>
    public InquiryForeignKeyAttribute(string columnName, string referencedTable, string referencedColumn)
        : base(columnName)
    {
        ValidateReference(referencedTable, referencedColumn);
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

    private static void ValidateReference(string referencedTable, string referencedColumn)
    {
        if (string.IsNullOrWhiteSpace(referencedTable))
        {
            throw new ArgumentException("Referenced table cannot be empty.", nameof(referencedTable));
        }

        if (string.IsNullOrWhiteSpace(referencedColumn))
        {
            throw new ArgumentException("Referenced column cannot be empty.", nameof(referencedColumn));
        }
    }
}
