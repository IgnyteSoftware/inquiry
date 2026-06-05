using Inquiry.Generators.Abstractions;
using System.Collections.Generic;
using System.Linq;

namespace Inquiry.MySql.Analyzer;

/// <summary>
/// MySQL/MariaDB SQL builder. Backtick identifier quoting, <c>ON DUPLICATE KEY UPDATE</c> upsert,
/// and — because neither MySQL nor MariaDB supports <c>RETURNING</c> on DML — an emulated returning
/// path: a two-statement batch ending in a <c>SELECT</c>. The trailing <c>SELECT</c> is the first
/// (and only) row-returning result set, so the existing pipeline (which reads result set #1 under
/// <c>CommandBehavior.SingleResult</c>) consumes it with zero runtime changes — the same shape
/// SqlServer's IF/INSERT/SELECT upsert and PostgreSQL's CTE returning already rely on.
/// </summary>
internal sealed class MySqlSqlBuilder : SqlBuilder
{
    public override string DialectName => "MySql";

    public override string QuoteIdentifier(string identifier)
        => "`" + identifier.Replace("`", "``") + "`";

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
        // MATCH ... AGAINST natural-language search (requires a FULLTEXT index on the columns).
        var cols = string.Join(", ", searchColumns.Select(c => QuoteIdentifier(c.ColumnName)));
        var predicate = "MATCH(" + cols + ") AGAINST (" + ParameterName("searchTerm") + " IN NATURAL LANGUAGE MODE)";
        return "SELECT " + context.SelectColumns + " FROM " + context.Table + " WHERE " + AppendWhere(predicate, context.SoftDeleteActivePredicate);
    }

    public override string BuildInsertSql(SqlBuildContext context)
    {
        if (context.InsertableColumns.Count == 0)
        {
            // MySQL has no DEFAULT VALUES; the empty-column form inserts an all-defaults row.
            return "INSERT INTO " + context.Table + " () VALUES ()";
        }

        return "INSERT INTO " + context.Table
            + " (" + context.InsertColumns + ") VALUES (" + context.InsertParameters + ")";
    }

    public override string BuildInsertReturningSql(SqlBuildContext context)
    {
        if (DatabaseSuppliesGuidKey(context))
        {
            return BuildGuidKeyInsertReturningSql(context);
        }

        return BuildInsertSql(context) + "; " + BuildReturningSelect(context);
    }

    public override string BuildUpdateSql(SqlBuildContext context)
        => "UPDATE " + context.Table + " SET " + context.SetClausesWithVersion
            + " WHERE " + AppendWhere(context.KeyWhereClause, context.ConcurrencyWhereClause);

    public override string BuildUpdateReturningSql(SqlBuildContext context)
        => BuildUpdateSql(context) + "; SELECT " + context.SelectColumns + " FROM " + context.Table + " WHERE " + context.KeyWhereClause;

    public override string BuildDeleteByKeySql(SqlBuildContext context)
        => "DELETE FROM " + context.Table + " WHERE " + AppendWhere(context.KeyWhereClause, context.ConcurrencyWhereClause);

    public override string BuildUpsertSql(SqlBuildContext context)
    {
        if (DatabaseSuppliesGuidKey(context))
        {
            return BuildGuidKeyUpsertSql(context);
        }

        if (DatabaseMaySupplyKey(context))
        {
            return BuildGeneratedKeyUpsertSql(context);
        }

        return "INSERT INTO " + context.Table + " (" + context.InsertColumns + ") VALUES (" + context.InsertParameters + ") " +
            "ON DUPLICATE KEY UPDATE " + OnDuplicateKeyAssignments(context);
    }

    public override string BuildUpsertReturningSql(SqlBuildContext context)
    {
        if (DatabaseSuppliesGuidKey(context))
        {
            return BuildGuidKeyUpsertReturningSql(context);
        }

        if (DatabaseMaySupplyKey(context))
        {
            // The trailing returning SELECT reads the row back via LAST_INSERT_ID(). On the INSERT branch
            // that is the freshly generated key; on the ON DUPLICATE KEY UPDATE branch no auto-increment
            // fires, so the upsert sets it explicitly with `key = LAST_INSERT_ID(key)` (the standard MySQL
            // trick) — LAST_INSERT_ID() then returns the existing row's key, so the SELECT finds it.
            return BuildGeneratedKeyUpsertSql(context, echoKeyForReturning: true) + "; " + BuildReturningSelect(context);
        }

        return BuildUpsertSql(context) + "; SELECT " + context.SelectColumns + " FROM " + context.Table + " WHERE " + context.KeyWhereClause;
    }

    private string BuildGeneratedKeyUpsertSql(SqlBuildContext context, bool echoKeyForReturning = false)
    {
        var keyColumn = context.QuotedKeyColumns[0];
        var keyParameter = context.KeyParameters[0];
        var explicitInsertColumns = JoinSql(keyColumn, context.InsertColumns);
        var explicitInsertParameters = JoinSql(keyParameter, context.InsertParameters);

        var assignments = OnDuplicateKeyAssignments(context);
        if (echoKeyForReturning)
        {
            // Set LAST_INSERT_ID() to this row's existing key on the UPDATE branch so the returning SELECT
            // (keyed on LAST_INSERT_ID()) reads the updated row back rather than a stale/empty result.
            assignments = JoinSql(keyColumn + " = LAST_INSERT_ID(" + keyColumn + ")", assignments);
        }

        return "INSERT INTO " + context.Table + " (" + explicitInsertColumns + ") VALUES (" + explicitInsertParameters + ") " +
            "ON DUPLICATE KEY UPDATE " + assignments;
    }

    /// <summary>
    /// Emulated-returning trailing <c>SELECT</c> for the integer/client-key paths. A single
    /// database-supplied (AUTO_INCREMENT) key is read back via session-scoped <c>LAST_INSERT_ID()</c>;
    /// otherwise the row is selected by its key predicate. GUID database-supplied keys never reach here —
    /// they branch to the <c>@_inquiry_genkey</c> user-variable methods before this is called.
    /// </summary>
    private string BuildReturningSelect(SqlBuildContext context)
    {
        var keyPredicate = DatabaseMaySupplyKey(context)
            ? context.QuotedKeyColumns[0] + " = LAST_INSERT_ID()"
            : context.KeyWhereClause;

        return "SELECT " + context.SelectColumns + " FROM " + context.Table + " WHERE " + keyPredicate;
    }

    /// <summary>The session user variable holding the (generated or explicit) GUID key for the returning batch.</summary>
    private const string GeneratedGuidKeyVariable = "@_inquiry_genkey";

    /// <summary>
    /// True when the single key is a database-supplied GUID. MySQL's LAST_INSERT_ID() returning trick only
    /// works for AUTO_INCREMENT, so a GUID key generated by the database needs a different mechanism.
    /// </summary>
    private static bool DatabaseSuppliesGuidKey(SqlBuildContext context)
        => DatabaseMaySupplyKey(context) && context.KeyColumns[0].TypeClass == DbTypeClass.Guid;

    // Non-returning GUID-key upsert: COALESCE(@key, UUID()) lets an explicit key pass through and a null
    // key be generated server-side. No user variable is needed because nothing is read back.
    private string BuildGuidKeyUpsertSql(SqlBuildContext context)
    {
        var keyColumn = context.QuotedKeyColumns[0];
        var keyValue = "COALESCE(" + context.KeyParameters[0] + ", UUID())";
        var insertColumns = JoinSql(keyColumn, context.InsertColumns);
        var insertValues = JoinSql(keyValue, context.InsertParameters);

        return "INSERT INTO " + context.Table + " (" + insertColumns + ") VALUES (" + insertValues + ") " +
            "ON DUPLICATE KEY UPDATE " + OnDuplicateKeyAssignments(context);
    }

    // Returning GUID-key upsert: capture the (generated or explicit) key in a user variable so the trailing
    // SELECT can read the new/updated row back by it. Requires AllowUserVariables on the connection.
    private string BuildGuidKeyUpsertReturningSql(SqlBuildContext context)
    {
        var keyColumn = context.QuotedKeyColumns[0];
        var insertColumns = JoinSql(keyColumn, context.InsertColumns);
        var insertValues = JoinSql(GeneratedGuidKeyVariable, context.InsertParameters);

        return "SET " + GeneratedGuidKeyVariable + " = COALESCE(" + context.KeyParameters[0] + ", UUID()); " +
            "INSERT INTO " + context.Table + " (" + insertColumns + ") VALUES (" + insertValues + ") " +
            "ON DUPLICATE KEY UPDATE " + OnDuplicateKeyAssignments(context) + "; " +
            "SELECT " + context.SelectColumns + " FROM " + context.Table + " WHERE " + keyColumn + " = " + GeneratedGuidKeyVariable;
    }

    // Returning GUID-key INSERT: like the upsert form but without ON DUPLICATE KEY UPDATE — capture the
    // (generated or explicit) key in a user variable so the trailing SELECT can read the inserted row back.
    // Needed because MySQL has no RETURNING and LAST_INSERT_ID() cannot read back a server-generated UUID.
    private string BuildGuidKeyInsertReturningSql(SqlBuildContext context)
    {
        var keyColumn = context.QuotedKeyColumns[0];
        var insertColumns = JoinSql(keyColumn, context.InsertColumns);
        var insertValues = JoinSql(GeneratedGuidKeyVariable, context.InsertParameters);

        return "SET " + GeneratedGuidKeyVariable + " = COALESCE(" + context.KeyParameters[0] + ", UUID()); " +
            "INSERT INTO " + context.Table + " (" + insertColumns + ") VALUES (" + insertValues + "); " +
            "SELECT " + context.SelectColumns + " FROM " + context.Table + " WHERE " + keyColumn + " = " + GeneratedGuidKeyVariable;
    }

    // The MySQL-equivalent of SqlBuildContext.SetClauses, but assigning each column from the
    // attempted-insert value (VALUES(col)) rather than from a bound parameter — so the conflict
    // branch updates with the same data the insert branch would have written. VALUES(col) is
    // deprecated in MySQL 8.0.20+ but still functional and is the only form MariaDB / MySQL 5.7
    // understand, so it is chosen deliberately for cross-engine compatibility.
    //
    // Exception: UseDatabaseDefault columns are omitted from the INSERT list so the database
    // default applies on the insert branch. VALUES(col) for those columns therefore resolves
    // to the column's default, NOT the entity's intended update value — silently reverting an
    // upsert UPDATE branch to the default. Bind the entity's parameter directly for those columns
    // instead; SelectMutationColumns(includeKey: true) — which drives the upsert binder — already
    // includes UseDatabaseDefault columns, so the parameter is available at the call site.
    private string OnDuplicateKeyAssignments(SqlBuildContext context)
        => string.Join(", ", context.Columns
            .Where(c => !c.IsKey && !c.IsGenerated)
            .Select(c =>
            {
                var quoted = QuoteIdentifier(c.ColumnName);
                return c.UseDatabaseDefault
                    ? quoted + " = " + ParameterName(c.PropertyName)
                    : quoted + " = VALUES(" + quoted + ")";
            }));

    private static string JoinSql(string first, string rest)
        => string.IsNullOrEmpty(rest) ? first : first + ", " + rest;

    // ---- DDL --------------------------------------------------------------------------------

    // MySQL cannot index LONGTEXT without a prefix length; a string key needs an explicit Length.
    public override bool RequiresBoundedStringKeys => true;

    protected override string MapColumnType(IColumn column) => column.TypeClass switch
    {
        DbTypeClass.Boolean => "TINYINT(1)",
        DbTypeClass.Byte => "TINYINT UNSIGNED",
        DbTypeClass.Int16 => "SMALLINT",
        DbTypeClass.Int32 => "INT",
        DbTypeClass.Int64 => "BIGINT",
        DbTypeClass.Single => "FLOAT",
        DbTypeClass.Double => "DOUBLE",
        DbTypeClass.Decimal => "DECIMAL(" + DecimalSpec(column, 18, 2) + ")",
        DbTypeClass.DateTime or DbTypeClass.DateTimeOffset => "DATETIME",
        DbTypeClass.Guid => "CHAR(36)",
        DbTypeClass.ByteArray => "LONGBLOB",
        // MySQL cannot index LONGTEXT; a bounded Length is required for PK/FK string columns.
        _ => column.Length > 0 ? "VARCHAR(" + column.Length + ")" : "LONGTEXT",
    };

    protected override string GeneratedKeyClause(IColumn column)
        => MapColumnType(column) + " AUTO_INCREMENT PRIMARY KEY";
}
