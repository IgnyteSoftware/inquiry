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
    /// Initializes a new instance of the <see cref="InquiryForeignKeyAttribute"/> class using a
    /// type reference. The source generator resolves the table name from the target's
    /// <see cref="InquiryTableAttribute"/> and the column from its <c>[InquiryKey]</c> property.
    /// </summary>
    public InquiryForeignKeyAttribute(Type referencedType)
    {
        ReferencedType = referencedType ?? throw new ArgumentNullException(nameof(referencedType));
        ReferencedTable = null!;
        ReferencedColumn = null!;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InquiryForeignKeyAttribute"/> class using a
    /// type reference with an explicit referenced column (property name on the target entity).
    /// </summary>
    public InquiryForeignKeyAttribute(Type referencedType, string referencedColumn)
    {
        ReferencedType = referencedType ?? throw new ArgumentNullException(nameof(referencedType));
        if (string.IsNullOrWhiteSpace(referencedColumn))
            throw new ArgumentException("Referenced column cannot be empty.", nameof(referencedColumn));
        ReferencedTable = null!;
        ReferencedColumn = referencedColumn;
    }

    /// <summary>
    /// Gets the referenced entity type when the type-safe constructor was used; otherwise <see langword="null"/>.
    /// </summary>
    public Type? ReferencedType { get; }

    /// <summary>
    /// Gets the referenced table name.
    /// </summary>
    public string ReferencedTable { get; }

    /// <summary>
    /// Gets the referenced column name.
    /// </summary>
    public string ReferencedColumn { get; }

    /// <summary>
    /// Gets or sets the schema that contains the referenced table.
    /// </summary>
    public string? ReferencedSchema
    {
        get => _referencedSchema;
        set
        {
            if (value is not null && string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Referenced schema cannot be empty.", nameof(ReferencedSchema));
            }

            _referencedSchema = value;
        }
    }

    private string? _referencedSchema;

    /// <summary>Gets or sets the physical foreign-key constraint name.</summary>
    public string? ConstraintName { get; set; }
    /// <summary>Gets or sets the action applied when the referenced row is deleted.</summary>
    public InquiryReferentialAction OnDelete { get; set; }
    /// <summary>Gets or sets the action applied when the referenced key is updated.</summary>
    public InquiryReferentialAction OnUpdate { get; set; }

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
