using Inquiry.Sql;

namespace Inquiry.PostgreSql;

/// <summary>
/// Provides PostgreSQL SQL naming, quoting, and upsert behavior for Inquiry generated statements.
/// </summary>
public sealed class PostgreSqlInquirySqlDialect : InquirySqlDialect
{
    /// <inheritdoc />
    public override string Name => "PostgreSql";

    /// <inheritdoc />
    public override string QuoteIdentifier(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            throw new ArgumentException("Identifier cannot be empty.", nameof(identifier));
        }

        return "\"" + identifier.Replace("\"", "\"\"") + "\"";
    }

    /// <inheritdoc />
    /// <remarks>
    /// Uses PostgreSQL 9.5+ <c>INSERT ... ON CONFLICT DO UPDATE</c> syntax.
    /// </remarks>
    public override string BuildUpsertSql(
        string table,
        string insertColumns,
        string insertParameters,
        string setClauses,
        string keyColumn,
        string keyParam)
    {
        return $"INSERT INTO {table} ({insertColumns}) VALUES ({insertParameters}) " +
               $"ON CONFLICT ({keyColumn}) DO UPDATE SET {setClauses}";
    }
}
