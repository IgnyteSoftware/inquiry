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
            + " WHERE " + context.KeyWhereClause;
    }

    /// <inheritdoc />
    public override string BuildSelectByFieldSql(InquirySqlBuildContext context, IReadOnlyList<InquirySqlColumn> columns)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));
        if (columns is null) throw new ArgumentNullException(nameof(columns));
        if (columns.Count == 0) throw new ArgumentException("At least one column is required.", nameof(columns));

        var where = string.Join(" AND ", columns
            .Select(c => QuoteIdentifier(c.ColumnName) + " = " + ParameterName(c.PropertyName)));
        return "SELECT " + context.SelectColumns + " FROM " + context.Table + " WHERE " + where;
    }

    /// <inheritdoc />
    public override string BuildInsertSql(InquirySqlBuildContext context)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));
        EnsureCanInsert(context);
        if (context.InsertableColumns.Count == 0)
        {
            return "INSERT INTO " + context.Table + " DEFAULT VALUES";
        }

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
            + " WHERE " + context.KeyWhereClause;
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
        return "DELETE FROM " + context.Table + " WHERE " + context.KeyWhereClause;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Uses SQLite 3.24+ <c>INSERT ... ON CONFLICT DO UPDATE</c> syntax.
    /// </remarks>
    public override string BuildUpsertSql(InquirySqlBuildContext context)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));
        EnsureCanUpsert(context);
        if (DatabaseMaySupplyKey(context))
        {
            return BuildGeneratedKeyUpsertSql(context, returning: false);
        }

        return $"INSERT INTO {context.Table} ({context.InsertColumns}) VALUES ({context.InsertParameters}) " +
            $"ON CONFLICT ({JoinKeyColumns(context)}) DO UPDATE SET {context.SetClauses}";
    }

    /// <inheritdoc />
    public override string BuildUpsertReturningSql(InquirySqlBuildContext context)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));
        EnsureCanUpsert(context);
        if (DatabaseMaySupplyKey(context))
        {
            return BuildGeneratedKeyUpsertSql(context, returning: true);
        }

        return BuildUpsertSql(context) + " RETURNING " + context.SelectColumns;
    }

    private string BuildGeneratedKeyUpsertSql(InquirySqlBuildContext context, bool returning)
    {
        // Generated-key upsert only applies to single-PK entities (composite PKs reject
        // IsGenerated in CreateContext), so KeyColumns[0] is safe here.
        var keyColumn = context.QuotedKeyColumns[0];
        var keyParameter = context.KeyParameters[0];
        var explicitInsertColumns = JoinSql(keyColumn, context.InsertColumns);
        var explicitInsertParameters = JoinSql(keyParameter, context.InsertParameters);
        var returningClause = returning ? " RETURNING " + context.SelectColumns : string.Empty;

        return $"INSERT INTO {context.Table} ({explicitInsertColumns}) VALUES ({explicitInsertParameters}) " +
            $"ON CONFLICT ({keyColumn}) DO UPDATE SET {context.SetClauses}{returningClause}";
    }

    private static bool DatabaseMaySupplyKey(InquirySqlBuildContext context)
    {
        // Only single-PK entities reach the generated-key path; composite PKs reject
        // IsGenerated columns up front in CreateContext.
        if (context.KeyColumns.Count != 1) return false;
        var key = context.KeyColumns[0];
        return key.IsGenerated || key.UseDatabaseDefault;
    }

    private static string JoinKeyColumns(InquirySqlBuildContext context)
        => string.Join(", ", context.QuotedKeyColumns);

    private static string JoinSql(string first, string rest)
        => string.IsNullOrWhiteSpace(rest) ? first : first + ", " + rest;
}
