using Inquiry.Sql;

namespace Inquiry.Sqlite;

/// <summary>
/// Provides SQLite SQL naming, quoting, and upsert behavior for Inquiry generated statements.
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

    /// <inheritdoc />
    /// <remarks>
    /// SQLite uses <c>INSERT OR REPLACE</c> which deletes the existing row (triggering ON DELETE
    /// constraints) and inserts the new one. For conflict-safe upserts that only update changed
    /// columns, use <c>INSERT OR IGNORE / UPDATE</c> or <c>INSERT ... ON CONFLICT DO UPDATE</c>
    /// (SQLite 3.24+). This implementation uses the widely-compatible <c>INSERT OR REPLACE</c>.
    /// </remarks>
    public override string BuildUpsertSql(
        string table,
        string insertColumns,
        string insertParameters,
        string setClauses,
        string keyColumn,
        string keyParam)
    {
        return $"INSERT OR REPLACE INTO {table} ({insertColumns}) VALUES ({insertParameters})";
    }
}
