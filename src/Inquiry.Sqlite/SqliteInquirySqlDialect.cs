using Inquiry.Sql;

namespace Inquiry.Sqlite;

/// <summary>
/// Provides SQLite SQL naming, quoting, and statement generation for Inquiry.
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
    public override string BuildSelectAllSql(InquirySqlBuildContext context)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));
        return "SELECT " + context.SelectColumns + " FROM " + context.Table;
    }

    /// <inheritdoc />
    public override string BuildSelectByKeySql(InquirySqlBuildContext context)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));
        return "SELECT " + context.SelectColumns + " FROM " + context.Table
            + " WHERE " + context.QuotedKeyColumn + " = " + ParameterName("key");
    }

    /// <inheritdoc />
    public override string BuildSelectByFieldSql(InquirySqlBuildContext context, InquirySqlColumn column)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));
        if (column is null) throw new ArgumentNullException(nameof(column));
        return "SELECT " + context.SelectColumns + " FROM " + context.Table
            + " WHERE " + QuoteIdentifier(column.ColumnName) + " = " + ParameterName("value");
    }

    /// <inheritdoc />
    public override string BuildInsertSql(InquirySqlBuildContext context)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));
        EnsureCanInsert(context);
        return "INSERT INTO " + context.Table
            + " (" + context.InsertColumns + ") VALUES (" + context.InsertParameters + ")";
    }

    /// <inheritdoc />
    public override string BuildInsertReturningSql(InquirySqlBuildContext context)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));
        EnsureCanInsert(context);
        return BuildInsertSql(context) + " RETURNING " + context.SelectColumns;
    }

    /// <inheritdoc />
    public override string BuildUpdateSql(InquirySqlBuildContext context)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));
        EnsureCanUpdate(context);
        return "UPDATE " + context.Table + " SET " + context.SetClauses
            + " WHERE " + context.QuotedKeyColumn + " = " + context.KeyParameter;
    }

    /// <inheritdoc />
    public override string BuildUpdateReturningSql(InquirySqlBuildContext context)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));
        EnsureCanUpdate(context);
        return BuildUpdateSql(context) + " RETURNING " + context.SelectColumns;
    }

    /// <inheritdoc />
    public override string BuildDeleteByKeySql(InquirySqlBuildContext context)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));
        return "DELETE FROM " + context.Table
            + " WHERE " + context.QuotedKeyColumn + " = " + ParameterName("key");
    }

    /// <inheritdoc />
    /// <remarks>
    /// SQLite uses <c>INSERT OR REPLACE</c> which deletes the existing row (triggering ON DELETE
    /// constraints) and inserts the new one. For conflict-safe upserts that only update changed
    /// columns, use <c>INSERT OR IGNORE / UPDATE</c> or <c>INSERT ... ON CONFLICT DO UPDATE</c>
    /// (SQLite 3.24+). This implementation uses the widely-compatible <c>INSERT OR REPLACE</c>.
    /// </remarks>
    public override string BuildUpsertSql(InquirySqlBuildContext context)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));
        EnsureCanUpsert(context);
        if (context.KeyColumn.IsGenerated)
        {
            return BuildGeneratedKeyUpsertSql(context, returning: false);
        }

        return $"INSERT OR REPLACE INTO {context.Table} ({context.InsertColumns}) VALUES ({context.InsertParameters})";
    }

    /// <inheritdoc />
    public override string BuildUpsertReturningSql(InquirySqlBuildContext context)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));
        EnsureCanUpsert(context);
        if (context.KeyColumn.IsGenerated)
        {
            return BuildGeneratedKeyUpsertSql(context, returning: true);
        }

        return BuildUpsertSql(context) + " RETURNING " + context.SelectColumns;
    }

    private string BuildGeneratedKeyUpsertSql(InquirySqlBuildContext context, bool returning)
    {
        var explicitInsertColumns = context.QuotedKeyColumn + ", " + context.InsertColumns;
        var explicitInsertParameters = context.KeyParameter + ", " + context.InsertParameters;
        var returningClause = returning ? " RETURNING " + context.SelectColumns : string.Empty;

        return $"INSERT INTO {context.Table} ({explicitInsertColumns}) VALUES ({explicitInsertParameters}) " +
            $"ON CONFLICT ({context.QuotedKeyColumn}) DO UPDATE SET {context.SetClauses}{returningClause}";
    }
}
