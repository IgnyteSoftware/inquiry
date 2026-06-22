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
        => "SELECT " + context.SelectColumns + " FROM " + context.Table + WhereSuffix(context.ActiveRowPredicate);

    public override string BuildSelectByKeySql(SqlBuildContext context)
        => "SELECT " + context.SelectColumns + " FROM " + context.Table + " WHERE " + AppendWhere(context.KeyWhereClause, context.ActiveRowPredicate);

    public override string BuildSelectByFieldSql(SqlBuildContext context, IReadOnlyList<IColumn> filterColumns)
    {
        var where = string.Join(" AND ", filterColumns
            .Select(c => QuoteIdentifier(c.ColumnName) + " = " + ParameterName(c.PropertyName)));
        return "SELECT " + context.SelectColumns + " FROM " + context.Table + " WHERE " + AppendWhere(where, context.ActiveRowPredicate);
    }

    public override bool SupportsFullTextSearch => true;

    public override string BuildFullTextSearchSql(SqlBuildContext context, IReadOnlyList<IColumn> searchColumns)
    {
        // Concatenate the searched columns into one tsvector and match a plain (natural-language) query.
        var vector = string.Join(" || ' ' || ", searchColumns.Select(c => "coalesce(" + QuoteIdentifier(c.ColumnName) + ", '')"));
        var predicate = "to_tsvector('simple', " + vector + ") @@ plainto_tsquery('simple', " + ParameterName("searchTerm") + ")";
        return "SELECT " + context.SelectColumns + " FROM " + context.Table + " WHERE " + AppendWhere(predicate, context.ActiveRowPredicate);
    }

    /// <summary>PostgreSQL uses native boolean literals.</summary>
    public override string BooleanFalseLiteral => "FALSE";

    /// <summary>PostgreSQL uses native boolean literals.</summary>
    public override string BooleanTrueLiteral => "TRUE";

    /// <summary>PostgreSQL stamps the soft-delete (and restore) timestamp from <c>now()</c>.</summary>
    public override string CurrentTimestampExpression => "now()";

    /// <summary>
    /// PostgreSQL binds IN collections as a single native array parameter: the SQL stays
    /// <c>col = ANY(@name)</c> for every list length, so server-side prepared statements remain
    /// reusable and the per-element parameter cap does not apply to IN lists.
    /// </summary>
    public override bool UseArrayInParameters => true;

    /// <summary>PostgreSQL bulk inserts ride binary COPY via the provider-registered copier.</summary>
    public override bool SupportsBulkCopy => true;

    /// <inheritdoc cref="UseArrayInParameters"/>
    protected override string RenderIn(string quotedColumn, string parameterName)
        => quotedColumn + " = ANY(" + parameterName + ")";

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
            OnConflictClause(JoinKeyColumns(context), context.SetClauses);
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
        // The null-key branch inserts only the non-key columns and lets the sequence supply the key. A
        // key-only entity has none, which would emit an invalid empty `() SELECT`. That branch is also
        // unreachable — a nullable key routes a null value to the plain insert, and a non-nullable key can
        // never be null — so omit it entirely when there are no insert columns.
        var hasGeneratedBranch = context.InsertableColumns.Count > 0;

        if (!returning)
        {
            var explicitInsert =
                "INSERT INTO " + context.Table + " (" + explicitInsertColumns + ") " +
                "SELECT " + explicitInsertParameters + " WHERE " + keyParameter + " IS NOT NULL " +
                OnConflictClause(keyColumn, context.SetClauses) + ";";
            return hasGeneratedBranch
                ? "INSERT INTO " + context.Table + " (" + context.InsertColumns + ") SELECT " + context.InsertParameters + " WHERE " + keyParameter + " IS NULL; " + explicitInsert
                : explicitInsert;
        }

        var insUpsert =
            "ins_upsert AS (INSERT INTO " + context.Table + " (" + explicitInsertColumns + ") " +
            "SELECT " + explicitInsertParameters + " WHERE " + keyParameter + " IS NOT NULL " +
            OnConflictClause(keyColumn, context.SetClauses) + " " +
            "RETURNING " + context.SelectColumns + ")";
        return hasGeneratedBranch
            ? "WITH ins_gen AS (INSERT INTO " + context.Table + " (" + context.InsertColumns + ") " +
              "SELECT " + context.InsertParameters + " WHERE " + keyParameter + " IS NULL " +
              "RETURNING " + context.SelectColumns + "), " +
              insUpsert + " " +
              "SELECT " + context.SelectColumns + " FROM ins_gen UNION ALL " +
              "SELECT " + context.SelectColumns + " FROM ins_upsert"
            : "WITH " + insUpsert + " SELECT " + context.SelectColumns + " FROM ins_upsert";
    }

    // An entity with no updatable non-key columns yields an empty SET clause; emit DO NOTHING (a conflict
    // is a valid no-op — "insert if absent") instead of the invalid `DO UPDATE SET ` with an empty body.
    private static string OnConflictClause(string conflictTarget, string setClauses)
        => setClauses.Length == 0
            ? "ON CONFLICT (" + conflictTarget + ") DO NOTHING"
            : "ON CONFLICT (" + conflictTarget + ") DO UPDATE SET " + setClauses;

    private static string JoinKeyColumns(SqlBuildContext context)
        => string.Join(", ", context.QuotedKeyColumns);

    private static string JoinSql(string first, string rest)
        => string.IsNullOrEmpty(rest) ? first : first + ", " + rest;

    // ---- DDL --------------------------------------------------------------------------------

    /// <summary>PostgreSQL computed columns must be typed and STORED.</summary>
    protected override string RenderComputedColumn(IColumn column)
        => ColumnType(column) + " GENERATED ALWAYS AS (" + column.ComputedExpression + ") STORED";

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
        DbTypeClass.DateOnly => "DATE",
        DbTypeClass.TimeOnly => "TIME",
        DbTypeClass.Guid => "UUID",
        DbTypeClass.ByteArray => "BYTEA",
        _ => column.Length > 0 ? "VARCHAR(" + column.Length + ")" : "TEXT",
    };

    // PostgreSQL identity uses SERIAL / BIGSERIAL (which create the backing sequence) rather than an
    // explicit type + IDENTITY clause, matching the conventional Northwind mapping.
    protected override string GeneratedKeyClause(IColumn column)
        => (column.TypeClass == DbTypeClass.Int64 ? "BIGSERIAL" : "SERIAL") + " PRIMARY KEY";

    protected override bool SupportsCreateIndexIfNotExists => true;

    // PostgreSQL extracts JSON text with the #>> path operator (a different path syntax than the SQL/JSON
    // `$.a.b` form the other dialects take), so translate the path to its `{a,b}` array literal. The column
    // is cast to jsonb so the operator applies whether it is stored as text or json/jsonb.
    protected override string RenderJsonPathExtract(string quotedColumn, string jsonPath)
        => "(" + quotedColumn + ")::jsonb #>> '" + JsonPathToPostgresTextPath(jsonPath) + "'";
}
