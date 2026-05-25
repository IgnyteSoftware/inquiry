namespace Inquiry.Sql;

/// <summary>
/// Provider-specific SQL naming, quoting, and statement generation for Inquiry.
/// </summary>
/// <remarks>
/// Each provider package implements this class to own every SQL string Inquiry produces.
/// The base type supplies only the dialect-agnostic plumbing — identifier/parameter naming,
/// validation, and the context object that statement builders consume. All concrete SQL
/// bodies live in the provider package and can be tuned independently.
/// </remarks>
public abstract class InquirySqlDialect
{
    /// <summary>Gets the dialect name.</summary>
    public abstract string Name { get; }

    /// <summary>Formats a logical parameter name for the provider.</summary>
    public virtual string ParameterName(string logicalName)
    {
        if (string.IsNullOrWhiteSpace(logicalName))
        {
            throw new ArgumentException("Parameter name cannot be empty.", nameof(logicalName));
        }

        return "@" + logicalName;
    }

    /// <summary>Formats a table name and optional schema for the provider.</summary>
    public string QuoteTable(string? schema, string tableName)
    {
        if (string.IsNullOrWhiteSpace(tableName))
        {
            throw new ArgumentException("Table name cannot be empty.", nameof(tableName));
        }

        return string.IsNullOrWhiteSpace(schema)
            ? QuoteIdentifier(tableName)
            : QuoteIdentifier(schema!) + "." + QuoteIdentifier(tableName);
    }

    /// <summary>Quotes an identifier for the provider.</summary>
    public abstract string QuoteIdentifier(string identifier);

    /// <summary>
    /// Validates column metadata and returns a precomputed <see cref="InquirySqlBuildContext"/>
    /// that the <c>Build*Sql</c> methods consume. Called once per (entity, dialect) pair —
    /// usually in the generated store's constructor.
    /// </summary>
    public InquirySqlBuildContext CreateContext(string? schema, string tableName, IReadOnlyList<InquirySqlColumn> columns)
    {
        if (columns is null)
        {
            throw new ArgumentNullException(nameof(columns));
        }

        if (columns.Count == 0)
        {
            throw new ArgumentException("At least one column is required.", nameof(columns));
        }

        var keys = columns.Where(c => c.IsKey).ToArray();
        if (keys.Length == 0)
        {
            throw new ArgumentException("Columns must contain exactly one key column; none were marked as a key.", nameof(columns));
        }

        if (keys.Length > 1)
        {
            throw new ArgumentException("Columns must contain exactly one key column; multiple columns are marked as keys.", nameof(columns));
        }

        var key = keys[0];
        var insertableColumns = columns.Where(c => !c.IsGenerated).ToArray();
        var table = QuoteTable(schema, tableName);
        var selectColumns = string.Join(", ", columns.Select(c => QuoteIdentifier(c.ColumnName)));
        var insertColumns = string.Join(", ", insertableColumns.Select(c => QuoteIdentifier(c.ColumnName)));
        var insertParameters = string.Join(", ", insertableColumns.Select(c => ParameterName(c.PropertyName)));
        var setClauses = string.Join(", ", columns
            .Where(c => !c.IsKey && !c.IsGenerated)
            .Select(c => QuoteIdentifier(c.ColumnName) + " = " + ParameterName(c.PropertyName)));
        var quotedKeyColumn = QuoteIdentifier(key.ColumnName);
        var keyParam = ParameterName(key.PropertyName);

        return new InquirySqlBuildContext(
            table: table,
            columns: columns,
            keyColumn: key,
            insertableColumns: insertableColumns,
            selectColumns: selectColumns,
            insertColumns: insertColumns,
            insertParameters: insertParameters,
            setClauses: setClauses,
            quotedKeyColumn: quotedKeyColumn,
            keyParameter: keyParam);
    }

    /// <summary>Throws if the context cannot produce a valid INSERT statement.</summary>
    protected static void EnsureCanInsert(InquirySqlBuildContext context)
    {
        if (context.InsertableColumns.Count == 0)
        {
            throw new InvalidOperationException("Cannot build INSERT SQL because all mapped columns are database-generated.");
        }
    }

    /// <summary>Throws if the context cannot produce a valid UPDATE SET clause.</summary>
    protected static void EnsureCanUpdate(InquirySqlBuildContext context)
    {
        if (string.IsNullOrWhiteSpace(context.SetClauses))
        {
            throw new InvalidOperationException("Cannot build UPDATE SQL because the entity has no non-key, non-generated columns to update.");
        }
    }

    /// <summary>Throws if the context cannot produce a valid provider upsert statement.</summary>
    protected static void EnsureCanUpsert(InquirySqlBuildContext context)
    {
        EnsureCanInsert(context);
        EnsureCanUpdate(context);

        if (context.KeyColumn.IsGenerated)
        {
            throw new InvalidOperationException("Cannot build UPSERT SQL for an entity whose key is database-generated.");
        }
    }

    /// <summary>Builds the SELECT-all statement.</summary>
    public abstract string BuildSelectAllSql(InquirySqlBuildContext context);

    /// <summary>Builds the SELECT-one-by-key statement.</summary>
    public abstract string BuildSelectByKeySql(InquirySqlBuildContext context);

    /// <summary>Builds a SELECT statement filtered by an arbitrary column.</summary>
    public abstract string BuildSelectByFieldSql(InquirySqlBuildContext context, InquirySqlColumn column);

    /// <summary>Builds the INSERT statement.</summary>
    public abstract string BuildInsertSql(InquirySqlBuildContext context);

    /// <summary>Builds the INSERT statement and returns the database row after mutation.</summary>
    public abstract string BuildInsertReturningSql(InquirySqlBuildContext context);

    /// <summary>Builds the UPDATE-by-key statement.</summary>
    public abstract string BuildUpdateSql(InquirySqlBuildContext context);

    /// <summary>Builds the UPDATE-by-key statement and returns the database row after mutation.</summary>
    public abstract string BuildUpdateReturningSql(InquirySqlBuildContext context);

    /// <summary>Builds the DELETE-by-key statement.</summary>
    public abstract string BuildDeleteByKeySql(InquirySqlBuildContext context);

    /// <summary>Builds a provider-specific upsert (insert-or-update) statement.</summary>
    public abstract string BuildUpsertSql(InquirySqlBuildContext context);

    /// <summary>Builds a provider-specific upsert statement and returns the database row after mutation.</summary>
    public abstract string BuildUpsertReturningSql(InquirySqlBuildContext context);
}
