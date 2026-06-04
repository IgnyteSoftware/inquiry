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
        => "SELECT " + context.SelectColumns + " FROM " + context.Table + WhereSuffix(context.SoftDeleteActivePredicate);

    public override string BuildSelectByKeySql(SqlBuildContext context)
        => "SELECT " + context.SelectColumns + " FROM " + context.Table + " WHERE " + AppendWhere(context.KeyWhereClause, context.SoftDeleteActivePredicate);

    public override string BuildSelectByFieldSql(SqlBuildContext context, IReadOnlyList<IColumn> filterColumns)
    {
        var where = string.Join(" AND ", filterColumns
            .Select(c => QuoteIdentifier(c.ColumnName) + " = " + ParameterName(c.PropertyName)));
        return "SELECT " + context.SelectColumns + " FROM " + context.Table + " WHERE " + AppendWhere(where, context.SoftDeleteActivePredicate);
    }

    public override bool SupportsFullTextSearch => true;

    public override string BuildFullTextSearchSql(SqlBuildContext context, IReadOnlyList<IColumn> searchColumns)
    {
        // Concatenate the searched columns into one tsvector and match a plain (natural-language) query.
        var vector = string.Join(" || ' ' || ", searchColumns.Select(c => "coalesce(" + QuoteIdentifier(c.ColumnName) + ", '')"));
        var predicate = "to_tsvector('simple', " + vector + ") @@ plainto_tsquery('simple', " + ParameterName("searchTerm") + ")";
        return "SELECT " + context.SelectColumns + " FROM " + context.Table + " WHERE " + AppendWhere(predicate, context.SoftDeleteActivePredicate);
    }

    /// <summary>PostgreSQL uses native boolean literals for the soft-delete flag.</summary>
    public override string SoftDeleteFalseLiteral => "FALSE";

    /// <summary>PostgreSQL uses native boolean literals for the soft-delete flag.</summary>
    public override string SoftDeleteTrueLiteral => "TRUE";

    /// <summary>PostgreSQL stamps the soft-delete (and restore) timestamp from <c>now()</c>.</summary>
    public override string CurrentTimestampExpression => "now()";

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
        => "UPDATE " + context.Table + " SET " + context.SetClausesWithVersion
            + " WHERE " + AppendWhere(context.KeyWhereClause, context.ConcurrencyWhereClause);

    public override string BuildUpdateReturningSql(SqlBuildContext context)
        => BuildUpdateSql(context) + " RETURNING " + context.SelectColumns;

    public override string BuildDeleteByKeySql(SqlBuildContext context)
        => "DELETE FROM " + context.Table + " WHERE " + AppendWhere(context.KeyWhereClause, context.ConcurrencyWhereClause);

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
                "INSERT INTO " + context.Table + generatedInsertColumns + "; " +
                "INSERT INTO " + context.Table + " (" + explicitInsertColumns + ") " +
                "SELECT " + explicitInsertParameters + " WHERE " + keyParameter + " IS NOT NULL " +
                "ON CONFLICT (" + keyColumn + ") DO UPDATE SET " + context.SetClauses + ";";
        }

        return
            "WITH ins_gen AS (INSERT INTO " + context.Table + " (" + context.InsertColumns + ") " +
            "SELECT " + context.InsertParameters + " WHERE " + keyParameter + " IS NULL " +
            "RETURNING " + context.SelectColumns + "), " +
            "ins_upsert AS (INSERT INTO " + context.Table + " (" + explicitInsertColumns + ") " +
            "SELECT " + explicitInsertParameters + " WHERE " + keyParameter + " IS NOT NULL " +
            "ON CONFLICT (" + keyColumn + ") DO UPDATE SET " + context.SetClauses + " " +
            "RETURNING " + context.SelectColumns + ") " +
            "SELECT " + context.SelectColumns + " FROM ins_gen UNION ALL " +
            "SELECT " + context.SelectColumns + " FROM ins_upsert";
    }

    private static string JoinKeyColumns(SqlBuildContext context)
        => string.Join(", ", context.QuotedKeyColumns);

    private static string JoinSql(string first, string rest)
        => string.IsNullOrEmpty(rest) ? first : first + ", " + rest;

    // ---- DDL --------------------------------------------------------------------------------

    protected override string MapColumnType(IColumn column) => column.TypeClass switch
    {
        DbTypeClass.Boolean => "BOOLEAN",
        DbTypeClass.Byte or DbTypeClass.Int16 => "SMALLINT",
        DbTypeClass.Int32 => "INTEGER",
        DbTypeClass.Int64 => "BIGINT",
        DbTypeClass.Single => "REAL",
        DbTypeClass.Double => "DOUBLE PRECISION",
        DbTypeClass.Decimal => "NUMERIC(" + DecimalSpec(column, 18, 2) + ")",
        DbTypeClass.DateTime => "TIMESTAMP",
        DbTypeClass.DateTimeOffset => "TIMESTAMPTZ",
        DbTypeClass.Guid => "UUID",
        DbTypeClass.ByteArray => "BYTEA",
        _ => column.Length > 0 ? "VARCHAR(" + column.Length + ")" : "TEXT",
    };

    // PostgreSQL identity uses SERIAL / BIGSERIAL (which create the backing sequence) rather than an
    // explicit type + IDENTITY clause, matching the conventional Northwind mapping.
    protected override string GeneratedKeyClause(IColumn column)
        => (column.TypeClass == DbTypeClass.Int64 ? "BIGSERIAL" : "SERIAL") + " PRIMARY KEY";

    protected override bool SupportsCreateIndexIfNotExists => true;
}
