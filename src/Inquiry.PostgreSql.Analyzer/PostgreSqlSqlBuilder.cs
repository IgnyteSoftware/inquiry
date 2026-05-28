using Inquiry.Generators.Abstractions;
using System.Collections.Generic;
using System.Linq;

namespace Inquiry.PostgreSql.Analyzer;

internal sealed class PostgreSqlSqlBuilder : SqlBuilder
{
    public override string DialectName => "PostgreSql";

    public override string QuoteIdentifier(string identifier)
        => "\"" + identifier.Replace("\"", "\"\"") + "\"";

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
        => BuildInsertSql(context) + " RETURNING " + context.SelectColumns;

    public override string BuildUpdateSql(SqlBuildContext context)
        => "UPDATE " + context.Table + " SET " + context.SetClauses + " WHERE " + context.KeyWhereClause;

    public override string BuildUpdateReturningSql(SqlBuildContext context)
        => BuildUpdateSql(context) + " RETURNING " + context.SelectColumns;

    public override string BuildDeleteByKeySql(SqlBuildContext context)
        => "DELETE FROM " + context.Table + " WHERE " + context.KeyWhereClause;

    public override string BuildUpsertSql(SqlBuildContext context)
    {
        if (DatabaseMaySupplyKey(context))
        {
            return BuildGeneratedKeyUpsertSql(context, returning: false);
        }

        return "INSERT INTO " + context.Table + " (" + context.InsertColumns + ") VALUES (" + context.InsertParameters + ") " +
            "ON CONFLICT (" + JoinKeyColumns(context) + ") DO UPDATE SET " + context.SetClauses;
    }

    public override string BuildUpsertReturningSql(SqlBuildContext context)
    {
        if (DatabaseMaySupplyKey(context))
        {
            return BuildGeneratedKeyUpsertSql(context, returning: true);
        }

        return BuildUpsertSql(context) + " RETURNING " + context.SelectColumns;
    }

    private string BuildGeneratedKeyUpsertSql(SqlBuildContext context, bool returning)
    {
        var keyColumn = context.QuotedKeyColumns[0];
        var keyParameter = context.KeyParameters[0];
        var explicitInsertColumns = JoinSql(keyColumn, context.InsertColumns);
        var explicitInsertParameters = JoinSql(keyParameter, context.InsertParameters);
        var generatedInsertColumns = " (" + context.InsertColumns + ") SELECT " + context.InsertParameters + " WHERE " + keyParameter + " IS NULL";

        if (!returning)
        {
            return
                "UPDATE " + context.Table + " SET " + context.SetClauses + " " +
                "WHERE " + keyParameter + " IS NOT NULL AND " + keyColumn + " = " + keyParameter + "; " +
                "INSERT INTO " + context.Table + generatedInsertColumns + "; " +
                "INSERT INTO " + context.Table + " (" + explicitInsertColumns + ") " +
                "SELECT " + explicitInsertParameters + " WHERE " + keyParameter + " IS NOT NULL " +
                "AND NOT EXISTS (SELECT 1 FROM " + context.Table + " WHERE " + keyColumn + " = " + keyParameter + ");";
        }

        var generatedReturningInsert =
            "INSERT INTO " + context.Table + " (" + context.InsertColumns + ") " +
            "SELECT " + context.InsertParameters + " WHERE " + keyParameter + " IS NULL " +
            "RETURNING " + context.SelectColumns;

        return
            "WITH updated AS (UPDATE " + context.Table + " SET " + context.SetClauses + " " +
            "WHERE " + keyParameter + " IS NOT NULL AND " + keyColumn + " = " + keyParameter + " " +
            "RETURNING " + context.SelectColumns + "), " +
            "inserted_generated AS (" + generatedReturningInsert + "), " +
            "inserted_explicit AS (INSERT INTO " + context.Table + " (" + explicitInsertColumns + ") " +
            "SELECT " + explicitInsertParameters + " WHERE " + keyParameter + " IS NOT NULL AND NOT EXISTS (SELECT 1 FROM updated) " +
            "RETURNING " + context.SelectColumns + ") " +
            "SELECT " + context.SelectColumns + " FROM updated UNION ALL " +
            "SELECT " + context.SelectColumns + " FROM inserted_generated UNION ALL " +
            "SELECT " + context.SelectColumns + " FROM inserted_explicit";
    }

    private static string JoinKeyColumns(SqlBuildContext context)
        => string.Join(", ", context.QuotedKeyColumns);

    private static string JoinSql(string first, string rest)
        => string.IsNullOrEmpty(rest) ? first : first + ", " + rest;
}
