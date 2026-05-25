using Inquiry.Sql;

namespace Inquiry.SqlServer;

/// <summary>
/// Provides SQL Server SQL naming, quoting, and statement generation for Inquiry.
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
        if (context.InsertableColumns.Count == 0)
        {
            return "INSERT INTO " + context.Table
                + " OUTPUT " + InsertedColumns(context)
                + " DEFAULT VALUES";
        }

        return "INSERT INTO " + context.Table
            + " (" + context.InsertColumns + ") OUTPUT " + InsertedColumns(context)
            + " VALUES (" + context.InsertParameters + ")";
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
        return "UPDATE " + context.Table + " SET " + context.SetClauses
            + " OUTPUT " + InsertedColumns(context)
            + " WHERE " + context.KeyWhereClause;
    }

    /// <inheritdoc />
    public override string BuildDeleteByKeySql(InquirySqlBuildContext context)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));
        return "DELETE FROM " + context.Table + " WHERE " + context.KeyWhereClause;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Uses a <c>MERGE</c> statement to atomically insert or update a single row.
    /// </remarks>
    public override string BuildUpsertSql(InquirySqlBuildContext context)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));
        EnsureCanUpsert(context);
        if (DatabaseMaySupplyKey(context))
        {
            return BuildGeneratedKeyUpsertSql(context, returning: false);
        }

        return
            $"MERGE INTO {context.Table} AS target " +
            $"USING ({BuildSourceSelect(context)}) AS source ON {BuildSourceJoin(context)} " +
            $"WHEN MATCHED THEN UPDATE SET {context.SetClauses} " +
            $"WHEN NOT MATCHED THEN INSERT ({context.InsertColumns}) VALUES ({context.InsertParameters});";
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

        return
            $"MERGE INTO {context.Table} AS target " +
            $"USING ({BuildSourceSelect(context)}) AS source ON {BuildSourceJoin(context)} " +
            $"WHEN MATCHED THEN UPDATE SET {context.SetClauses} " +
            $"WHEN NOT MATCHED THEN INSERT ({context.InsertColumns}) VALUES ({context.InsertParameters}) " +
            $"OUTPUT {InsertedColumns(context)};";
    }

    private string InsertedColumns(InquirySqlBuildContext context)
        => string.Join(", ", context.Columns.Select(c => "INSERTED." + QuoteIdentifier(c.ColumnName)));

    private string BuildGeneratedKeyUpsertSql(InquirySqlBuildContext context, bool returning)
    {
        // Generated-key upsert is single-PK only; composite PKs reject IsGenerated in CreateContext.
        var keyColumn = context.QuotedKeyColumns[0];
        var keyParameter = context.KeyParameters[0];
        var output = returning ? " OUTPUT " + InsertedColumns(context) : string.Empty;
        var explicitInsertColumns = JoinSql(keyColumn, context.InsertColumns);
        var explicitInsertParameters = JoinSql(keyParameter, context.InsertParameters);
        var generatedInsert = context.InsertableColumns.Count == 0
            ? $"INSERT INTO {context.Table}{output} DEFAULT VALUES; "
            : $"INSERT INTO {context.Table} ({context.InsertColumns}){output} VALUES ({context.InsertParameters}); ";

        return
            $"IF {keyParameter} IS NULL " +
            "BEGIN " +
            generatedInsert +
            "END " +
            $"ELSE IF EXISTS (SELECT 1 FROM {context.Table} WHERE {keyColumn} = {keyParameter}) " +
            "BEGIN " +
            $"UPDATE {context.Table} SET {context.SetClauses}{output} WHERE {keyColumn} = {keyParameter}; " +
            "END " +
            "ELSE " +
            "BEGIN " +
            $"INSERT INTO {context.Table} ({explicitInsertColumns}){output} VALUES ({explicitInsertParameters}); " +
            "END";
    }

    private static string BuildSourceSelect(InquirySqlBuildContext context)
    {
        // Produces "SELECT @<P0> AS k0, @<P1> AS k1" — the inner SELECT for the MERGE USING clause.
        // Positional aliases sidestep reserved-word collisions when a key column is named e.g. [Key].
        return "SELECT " + string.Join(", ", context.KeyParameters
            .Select((p, i) => p + " AS k" + i));
    }

    private static string BuildSourceJoin(InquirySqlBuildContext context)
    {
        // Produces "target.[k0col] = source.k0 AND target.[k1col] = source.k1".
        return string.Join(" AND ", context.QuotedKeyColumns
            .Select((q, i) => "target." + q + " = source.k" + i));
    }

    private static bool DatabaseMaySupplyKey(InquirySqlBuildContext context)
    {
        if (context.KeyColumns.Count != 1) return false;
        var key = context.KeyColumns[0];
        return key.IsGenerated || key.UseDatabaseDefault;
    }

    private static string JoinSql(string first, string rest)
        => string.IsNullOrWhiteSpace(rest) ? first : first + ", " + rest;
}
