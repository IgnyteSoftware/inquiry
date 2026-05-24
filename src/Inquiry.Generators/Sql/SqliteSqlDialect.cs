using System;

namespace Inquiry.Generators.Sql;

internal sealed class SqliteSqlDialect : InquirySqlDialect
{
    public static SqliteSqlDialect Instance { get; } = new();

    public override string Name => "Sqlite";

    public override string QuoteIdentifier(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            throw new ArgumentException("Identifier cannot be empty.", nameof(identifier));
        }

        return "\"" + identifier.Replace("\"", "\"\"") + "\"";
    }
}
