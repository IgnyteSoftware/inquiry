using Inquiry.Generators.Abstractions;
using System.Collections.Generic;
using System.Linq;

namespace Inquiry.SqlServer.Analyzer;

internal sealed class SqlServerSqlBuilder : SqlBuilder
{
    public override string DialectName => "SqlServer";

    public override string QuoteIdentifier(string identifier)
        => "[" + identifier.Replace("]", "]]") + "]";

    public override string BuildSelectAllSql(SqlBuildContext context)
        => "SELECT " + context.SelectColumns + " FROM " + context.Table;

    public override string BuildSelectByKeySql(SqlBuildContext context)
        => "SELECT " + context.SelectColumns + " FROM " + context.Table + " WHERE " + context.KeyWhereClause;

    public override string BuildSelectByFieldSql(SqlBuildContext context, IReadOnlyList<IColumn> filterColumns)
    {
        var where = string.Join(" AND ", filterColumns
            .Select(c => QuoteIdentifier(c.ColumnName) + " = " + ParameterName(c.PropertyName)));
        return "SELECT " + context.SelectColumns + " FROM " + context.Table + " WHERE " + where;
    }

    public override string BuildInsertSql(SqlBuildContext context)
    {
        if (context.InsertableColumns.Count == 0)
        {
            return "INSERT INTO " + context.Table + " DEFAULT VALUES";
        }

        return "INSERT INTO " + context.Table
            + " (" + context.InsertColumns + ") VALUES (" + context.InsertParameters + ")";
    }

    public override string BuildInsertReturningSql(SqlBuildContext context)
    {
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

    public override string BuildUpdateSql(SqlBuildContext context)
        => "UPDATE " + context.Table + " SET " + context.SetClauses + " WHERE " + context.KeyWhereClause;

    public override string BuildUpdateReturningSql(SqlBuildContext context)
        => "UPDATE " + context.Table + " SET " + context.SetClauses
            + " OUTPUT " + InsertedColumns(context)
            + " WHERE " + context.KeyWhereClause;

    public override string BuildDeleteByKeySql(SqlBuildContext context)
        => "DELETE FROM " + context.Table + " WHERE " + context.KeyWhereClause;

    public override string BuildUpsertSql(SqlBuildContext context)
    {
        if (DatabaseMaySupplyKey(context))
        {
            return BuildGeneratedKeyUpsertSql(context, returning: false);
        }

        return
            "MERGE INTO " + context.Table + " AS target " +
            "USING (" + BuildSourceSelect(context) + ") AS source ON " + BuildSourceJoin(context) + " " +
            "WHEN MATCHED THEN UPDATE SET " + context.SetClauses + " " +
            "WHEN NOT MATCHED THEN INSERT (" + context.InsertColumns + ") VALUES (" + context.InsertParameters + ");";
    }

    public override string BuildUpsertReturningSql(SqlBuildContext context)
    {
        if (DatabaseMaySupplyKey(context))
        {
            return BuildGeneratedKeyUpsertSql(context, returning: true);
        }

        return
            "MERGE INTO " + context.Table + " AS target " +
            "USING (" + BuildSourceSelect(context) + ") AS source ON " + BuildSourceJoin(context) + " " +
            "WHEN MATCHED THEN UPDATE SET " + context.SetClauses + " " +
            "WHEN NOT MATCHED THEN INSERT (" + context.InsertColumns + ") VALUES (" + context.InsertParameters + ") " +
            "OUTPUT " + InsertedColumns(context) + ";";
    }

    /// <summary>
    /// SQL Server offset pagination uses the ANSI <c>OFFSET … ROWS FETCH NEXT … ROWS ONLY</c> form,
    /// which requires a preceding ORDER BY (enforced in the generator for all dialects).
    /// </summary>
    public override string BuildPaginationClause(SqlSelectOptions options)
        => "OFFSET " + options.OffsetParameter + " ROWS FETCH NEXT " + options.LimitParameter + " ROWS ONLY";

    /// <summary>
    /// SQL Server lacks the row-value <c>(a, b) &gt; (@c0, @c1)</c> comparison, so a multi-column keyset
    /// renders the lexicographic OR-form <c>(a &gt; @c0) OR (a = @c0 AND b &gt; @c1)</c>. Single-column
    /// keysets fall back to the portable scalar form.
    /// </summary>
    public override string BuildKeysetPredicate(SqlSelectOptions options)
    {
        if (options.KeysetColumns.Count == 1)
        {
            return base.BuildKeysetPredicate(options);
        }

        var op = options.KeysetDescending ? " < " : " > ";
        var firstCursor = options.KeysetCursorParameters[0];
        var sb = new System.Text.StringBuilder();
        sb.Append('(').Append(firstCursor).Append(" IS NULL OR (");
        for (var i = 0; i < options.KeysetColumns.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(" OR ");
            }

            sb.Append('(');
            for (var j = 0; j < i; j++)
            {
                sb.Append(options.KeysetColumns[j]).Append(" = ").Append(options.KeysetCursorParameters[j]).Append(" AND ");
            }

            sb.Append(options.KeysetColumns[i]).Append(op).Append(options.KeysetCursorParameters[i]).Append(')');
        }

        sb.Append("))");
        return sb.ToString();
    }

    private string InsertedColumns(SqlBuildContext context)
        => string.Join(", ", context.Columns.Select(c => "INSERTED." + QuoteIdentifier(c.ColumnName)));

    private string BuildGeneratedKeyUpsertSql(SqlBuildContext context, bool returning)
    {
        var keyColumn = context.QuotedKeyColumns[0];
        var keyParameter = context.KeyParameters[0];
        var output = returning ? " OUTPUT " + InsertedColumns(context) : string.Empty;
        var explicitInsertColumns = JoinSql(keyColumn, context.InsertColumns);
        var explicitInsertParameters = JoinSql(keyParameter, context.InsertParameters);
        var generatedInsert = context.InsertableColumns.Count == 0
            ? "INSERT INTO " + context.Table + output + " DEFAULT VALUES; "
            : "INSERT INTO " + context.Table + " (" + context.InsertColumns + ")" + output + " VALUES (" + context.InsertParameters + "); ";

        return
            "IF " + keyParameter + " IS NULL " +
            "BEGIN " +
            generatedInsert +
            "END " +
            "ELSE IF EXISTS (SELECT 1 FROM " + context.Table + " WHERE " + keyColumn + " = " + keyParameter + ") " +
            "BEGIN " +
            "UPDATE " + context.Table + " SET " + context.SetClauses + output + " WHERE " + keyColumn + " = " + keyParameter + "; " +
            "END " +
            "ELSE " +
            "BEGIN " +
            "INSERT INTO " + context.Table + " (" + explicitInsertColumns + ")" + output + " VALUES (" + explicitInsertParameters + "); " +
            "END";
    }

    private static string BuildSourceSelect(SqlBuildContext context)
        => "SELECT " + string.Join(", ", context.KeyParameters.Select((p, i) => p + " AS k" + i));

    private static string BuildSourceJoin(SqlBuildContext context)
        => string.Join(" AND ", context.QuotedKeyColumns.Select((q, i) => "target." + q + " = source.k" + i));

    private static string JoinSql(string first, string rest)
        => string.IsNullOrEmpty(rest) ? first : first + ", " + rest;
}
