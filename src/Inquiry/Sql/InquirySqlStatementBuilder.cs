namespace Inquiry.Sql;

/// <summary>
/// Builds provider-specific SQL statements for mapped Inquiry entities by dispatching
/// each statement to the configured <see cref="InquirySqlDialect"/>.
/// </summary>
public sealed class InquirySqlStatementBuilder
{
    private readonly InquirySqlDialect _dialect;

    /// <summary>
    /// Initializes a new instance of the <see cref="InquirySqlStatementBuilder"/> class.
    /// </summary>
    public InquirySqlStatementBuilder(InquirySqlDialect dialect)
    {
        _dialect = dialect ?? throw new ArgumentNullException(nameof(dialect));
    }

    /// <summary>
    /// Builds SQL statements for a mapped table and columns.
    /// </summary>
    public InquirySqlStatementSet Build(string? schema, string tableName, IReadOnlyList<InquirySqlColumn> columns)
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
        if (insertableColumns.Length == 0)
        {
            throw new ArgumentException("At least one column must not be database-generated so INSERT can supply a value.", nameof(columns));
        }

        var table = _dialect.QuoteTable(schema, tableName);
        var selectColumns = string.Join(", ", columns.Select(c => _dialect.QuoteIdentifier(c.ColumnName)));
        var insertColumns = string.Join(", ", insertableColumns.Select(c => _dialect.QuoteIdentifier(c.ColumnName)));
        var insertParameters = string.Join(", ", insertableColumns.Select(c => _dialect.ParameterName(c.PropertyName)));
        var setClauses = string.Join(", ", columns
            .Where(c => !c.IsKey && !c.IsGenerated)
            .Select(c => _dialect.QuoteIdentifier(c.ColumnName) + " = " + _dialect.ParameterName(c.PropertyName)));
        var quotedKeyColumn = _dialect.QuoteIdentifier(key.ColumnName);
        var keyParam = _dialect.ParameterName(key.PropertyName);

        var context = new InquirySqlBuildContext(
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

        var selectByField = new Dictionary<string, string>(columns.Count, StringComparer.Ordinal);
        foreach (var column in columns)
        {
            selectByField[column.PropertyName] = _dialect.BuildSelectByFieldSql(context, column);
        }

        return new InquirySqlStatementSet(
            selectAll: _dialect.BuildSelectAllSql(context),
            selectByKey: _dialect.BuildSelectByKeySql(context),
            deleteByKey: _dialect.BuildDeleteByKeySql(context),
            insert: _dialect.BuildInsertSql(context),
            update: _dialect.BuildUpdateSql(context),
            upsert: _dialect.BuildUpsertSql(context),
            selectByField: selectByField);
    }
}
