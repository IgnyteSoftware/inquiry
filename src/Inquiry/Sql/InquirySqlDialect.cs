namespace Inquiry.Sql;

/// <summary>
/// Provides provider-specific SQL naming, quoting, and statement generation for Inquiry.
/// </summary>
/// <remarks>
/// The base class supplies only identifier/parameter naming primitives. All SQL statement
/// bodies are produced by the provider packages so each one can be tuned independently
/// (provider-optimized syntax, hints, RETURNING/OUTPUT clauses, etc.).
/// </remarks>
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

    /// <summary>Builds the SELECT-all statement.</summary>
    public abstract string BuildSelectAllSql(InquirySqlBuildContext context);

    /// <summary>Builds the SELECT-one-by-key statement.</summary>
    public abstract string BuildSelectByKeySql(InquirySqlBuildContext context);

    /// <summary>Builds a SELECT statement filtered by an arbitrary column.</summary>
    public abstract string BuildSelectByFieldSql(InquirySqlBuildContext context, InquirySqlColumn column);

    /// <summary>Builds the INSERT statement.</summary>
    public abstract string BuildInsertSql(InquirySqlBuildContext context);

    /// <summary>Builds the UPDATE-by-key statement.</summary>
    public abstract string BuildUpdateSql(InquirySqlBuildContext context);

    /// <summary>Builds the DELETE-by-key statement.</summary>
    public abstract string BuildDeleteByKeySql(InquirySqlBuildContext context);

    /// <summary>Builds a provider-specific upsert (insert-or-update) statement.</summary>
    public abstract string BuildUpsertSql(InquirySqlBuildContext context);
}
