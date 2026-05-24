namespace Inquiry;

/// <summary>
/// Builds provider-specific SQL statements for mapped Inquiry entities.
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

        var key = columns.Single(c => c.IsKey);
        var table = _dialect.QuoteTable(schema, tableName);
        var selectColumns = string.Join(", ", columns.Select(c => _dialect.QuoteIdentifier(c.ColumnName)));
        var insertColumns = string.Join(", ", columns.Select(c => _dialect.QuoteIdentifier(c.ColumnName)));
        var insertParameters = string.Join(", ", columns.Select(c => _dialect.ParameterName(c.PropertyName)));
        var setClauses = string.Join(", ", columns
            .Where(c => !c.IsKey)
            .Select(c => _dialect.QuoteIdentifier(c.ColumnName) + " = " + _dialect.ParameterName(c.PropertyName)));

        return new InquirySqlStatementSet(
            selectAll: "SELECT " + selectColumns + " FROM " + table,
            selectByKey: "SELECT " + selectColumns + " FROM " + table + " WHERE " + _dialect.QuoteIdentifier(key.ColumnName) + " = " + _dialect.ParameterName("key"),
            deleteByKey: "DELETE FROM " + table + " WHERE " + _dialect.QuoteIdentifier(key.ColumnName) + " = " + _dialect.ParameterName("key"),
            insert: "INSERT INTO " + table + " (" + insertColumns + ") VALUES (" + insertParameters + ")",
            update: "UPDATE " + table + " SET " + setClauses + " WHERE " + _dialect.QuoteIdentifier(key.ColumnName) + " = " + _dialect.ParameterName(key.PropertyName),
            selectByField: fieldColumn => "SELECT " + selectColumns + " FROM " + table + " WHERE " + _dialect.QuoteIdentifier(fieldColumn.ColumnName) + " = " + _dialect.ParameterName("value"));
    }
}
