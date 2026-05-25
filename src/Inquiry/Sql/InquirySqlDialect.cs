namespace Inquiry.Sql;

/// <summary>
/// Provides provider-specific SQL naming, quoting, and upsert behavior for Inquiry generated statements.
/// </summary>
public abstract class InquirySqlDialect
{
    /// <summary>
    /// Gets the dialect name.
    /// </summary>
    public abstract string Name { get; }

    /// <summary>
    /// Formats a logical parameter name for the provider.
    /// </summary>
    public virtual string ParameterName(string logicalName)
    {
        if (string.IsNullOrWhiteSpace(logicalName))
        {
            throw new ArgumentException("Parameter name cannot be empty.", nameof(logicalName));
        }

        return "@" + logicalName;
    }

    /// <summary>
    /// Formats a table name and optional schema for the provider.
    /// </summary>
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

    /// <summary>
    /// Quotes an identifier for the provider.
    /// </summary>
    public abstract string QuoteIdentifier(string identifier);

    /// <summary>
    /// Builds a provider-specific upsert (insert-or-update) statement.
    /// </summary>
    /// <param name="table">The fully-quoted table name.</param>
    /// <param name="insertColumns">Comma-separated quoted insert column list.</param>
    /// <param name="insertParameters">Comma-separated parameter list for insert values.</param>
    /// <param name="setClauses">Comma-separated SET clauses for the update path (col = @param).</param>
    /// <param name="keyColumn">The quoted key column name.</param>
    /// <param name="keyParam">The parameter name for the key value.</param>
    public abstract string BuildUpsertSql(
        string table,
        string insertColumns,
        string insertParameters,
        string setClauses,
        string keyColumn,
        string keyParam);
}
