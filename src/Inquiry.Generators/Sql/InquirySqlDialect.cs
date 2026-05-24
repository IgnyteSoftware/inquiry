using System;

namespace Inquiry.Generators.Sql;

internal abstract class InquirySqlDialect
{
    public abstract string Name { get; }

    public virtual string ParameterName(string logicalName)
    {
        if (string.IsNullOrWhiteSpace(logicalName))
        {
            throw new ArgumentException("Parameter name cannot be empty.", nameof(logicalName));
        }

        return "@" + logicalName;
    }

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

    public abstract string QuoteIdentifier(string identifier);
}
