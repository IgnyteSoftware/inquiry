namespace Inquiry.Sql;

/// <summary>
/// Precomputed inputs supplied to <see cref="InquirySqlDialect"/> when building each
/// statement for a mapped entity. Holds both ready-to-paste SQL fragments and the raw
/// column metadata so dialects can introspect when generating provider-optimized SQL.
/// </summary>
public sealed class InquirySqlBuildContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InquirySqlBuildContext"/> class.
    /// </summary>
    public InquirySqlBuildContext(
        string table,
        IReadOnlyList<InquirySqlColumn> columns,
        IReadOnlyList<InquirySqlColumn> keyColumns,
        IReadOnlyList<InquirySqlColumn> insertableColumns,
        string selectColumns,
        string insertColumns,
        string insertParameters,
        string setClauses,
        IReadOnlyList<string> quotedKeyColumns,
        IReadOnlyList<string> keyParameters,
        string keyWhereClause)
    {
        Table = table ?? throw new ArgumentNullException(nameof(table));
        Columns = columns ?? throw new ArgumentNullException(nameof(columns));
        KeyColumns = keyColumns ?? throw new ArgumentNullException(nameof(keyColumns));
        InsertableColumns = insertableColumns ?? throw new ArgumentNullException(nameof(insertableColumns));
        SelectColumns = selectColumns ?? throw new ArgumentNullException(nameof(selectColumns));
        InsertColumns = insertColumns ?? throw new ArgumentNullException(nameof(insertColumns));
        InsertParameters = insertParameters ?? throw new ArgumentNullException(nameof(insertParameters));
        SetClauses = setClauses ?? throw new ArgumentNullException(nameof(setClauses));
        QuotedKeyColumns = quotedKeyColumns ?? throw new ArgumentNullException(nameof(quotedKeyColumns));
        KeyParameters = keyParameters ?? throw new ArgumentNullException(nameof(keyParameters));
        KeyWhereClause = keyWhereClause ?? throw new ArgumentNullException(nameof(keyWhereClause));
    }

    /// <summary>Gets the fully-quoted target table (including schema if provided).</summary>
    public string Table { get; }

    /// <summary>Gets all mapped columns for the entity.</summary>
    public IReadOnlyList<InquirySqlColumn> Columns { get; }

    /// <summary>
    /// Gets the columns marked as the entity's primary key, in declaration order.
    /// Always contains at least one element; contains multiple for composite keys.
    /// </summary>
    public IReadOnlyList<InquirySqlColumn> KeyColumns { get; }

    /// <summary>Gets the columns that are supplied to INSERT (excludes database-generated values).</summary>
    public IReadOnlyList<InquirySqlColumn> InsertableColumns { get; }

    /// <summary>Gets a comma-separated, quoted list of every mapped column (for SELECT projections).</summary>
    public string SelectColumns { get; }

    /// <summary>Gets a comma-separated, quoted list of insertable columns.</summary>
    public string InsertColumns { get; }

    /// <summary>Gets a comma-separated parameter list matching <see cref="InsertColumns"/>.</summary>
    public string InsertParameters { get; }

    /// <summary>Gets a comma-separated <c>col = @param</c> list for UPDATE/upsert SET clauses (key + generated columns excluded).</summary>
    public string SetClauses { get; }

    /// <summary>Gets the quoted key column names, in declaration order.</summary>
    public IReadOnlyList<string> QuotedKeyColumns { get; }

    /// <summary>Gets the parameter names for the key values (e.g., <c>@OrderID</c>, <c>@ProductID</c>), in declaration order.</summary>
    public IReadOnlyList<string> KeyParameters { get; }

    /// <summary>
    /// Gets the precomputed WHERE-clause fragment matching all key columns
    /// (e.g., <c>"OrderID" = @OrderID AND "ProductID" = @ProductID</c>).
    /// </summary>
    public string KeyWhereClause { get; }
}
