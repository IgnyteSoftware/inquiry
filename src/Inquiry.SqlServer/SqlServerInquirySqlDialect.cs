using Inquiry.Sql;

namespace Inquiry.SqlServer;

/// <summary>
/// Provides SQL Server SQL naming, quoting, and upsert behavior for Inquiry generated statements.
/// </summary>
public sealed class SqlServerInquirySqlDialect : InquirySqlDialect
{
    /// <inheritdoc />
    public override string Name => "SqlServer";

    /// <inheritdoc />
    public override string QuoteIdentifier(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            throw new ArgumentException("Identifier cannot be empty.", nameof(identifier));
        }

        return "[" + identifier.Replace("]", "]]") + "]";
    }

    /// <inheritdoc />
    /// <remarks>
    /// Uses a <c>MERGE</c> statement to atomically insert or update a single row.
    /// </remarks>
    public override string BuildUpsertSql(
        string table,
        string insertColumns,
        string insertParameters,
        string setClauses,
        string keyColumn,
        string keyParam)
    {
        return
            $"MERGE INTO {table} AS target " +
            $"USING (SELECT {keyParam} AS k) AS source ON target.{keyColumn} = source.k " +
            $"WHEN MATCHED THEN UPDATE SET {setClauses} " +
            $"WHEN NOT MATCHED THEN INSERT ({insertColumns}) VALUES ({insertParameters});";
    }
}
