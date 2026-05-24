using Inquiry.Sql;

namespace Inquiry.Sqlite;

/// <summary>
/// Provides SQLite SQL naming and quoting behavior for Inquiry generated statements.
/// </summary>
public sealed class SqliteInquirySqlDialect : InquirySqlDialect
{
    /// <inheritdoc />
    public override string Name => "Sqlite";

    /// <inheritdoc />
    public override string QuoteIdentifier(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            throw new ArgumentException("Identifier cannot be empty.", nameof(identifier));
        }

        return "\"" + identifier.Replace("\"", "\"\"") + "\"";
    }
}
