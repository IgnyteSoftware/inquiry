namespace Inquiry.Sql;

/// <summary>
/// Provides provider-specific SQL naming and quoting behavior for Inquiry generated statements.
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
}
