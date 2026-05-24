using System;

namespace Inquiry.Generators.Sql;

internal sealed class SqlServerSqlDialect : InquirySqlDialect
{
    public static SqlServerSqlDialect Instance { get; } = new();

    public override string Name => "SqlServer";

    public override string QuoteIdentifier(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            throw new ArgumentException("Identifier cannot be empty.", nameof(identifier));
        }

        return "[" + identifier.Replace("]", "]]") + "]";
    }
}
