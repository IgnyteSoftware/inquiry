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
        InquirySqlColumn keyColumn,
        IReadOnlyList<InquirySqlColumn> insertableColumns,
        string selectColumns,
        string insertColumns,
        string insertParameters,
        string setClauses,
        string quotedKeyColumn,
        string keyParameter)
    {
        Table = table ?? throw new ArgumentNullException(nameof(table));
        Columns = columns ?? throw new ArgumentNullException(nameof(columns));
        KeyColumn = keyColumn ?? throw new ArgumentNullException(nameof(keyColumn));
        InsertableColumns = insertableColumns ?? throw new ArgumentNullException(nameof(insertableColumns));
        SelectColumns = selectColumns ?? throw new ArgumentNullException(nameof(selectColumns));
        InsertColumns = insertColumns ?? throw new ArgumentNullException(nameof(insertColumns));
        InsertParameters = insertParameters ?? throw new ArgumentNullException(nameof(insertParameters));
        SetClauses = setClauses ?? throw new ArgumentNullException(nameof(setClauses));
        QuotedKeyColumn = quotedKeyColumn ?? throw new ArgumentNullException(nameof(quotedKeyColumn));
        KeyParameter = keyParameter ?? throw new ArgumentNullException(nameof(keyParameter));
    }

    /// <summary>Gets the fully-quoted target table (including schema if provided).</summary>
    public string Table { get; }

    /// <summary>Gets all mapped columns for the entity.</summary>
    public IReadOnlyList<InquirySqlColumn> Columns { get; }

    /// <summary>Gets the column marked as the entity key.</summary>
    public InquirySqlColumn KeyColumn { get; }

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

    /// <summary>Gets the quoted key column name.</summary>
    public string QuotedKeyColumn { get; }

    /// <summary>Gets the parameter name for the key value (e.g., <c>@Key</c>).</summary>
    public string KeyParameter { get; }
}
