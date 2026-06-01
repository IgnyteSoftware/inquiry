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
        => BuildInsertSql(context) + "; " + BuildReturningSelect(context);

    public override string BuildUpdateSql(SqlBuildContext context)
        => "UPDATE " + context.Table + " SET " + context.SetClausesWithVersion
            + " WHERE " + AppendWhere(context.KeyWhereClause, context.ConcurrencyWhereClause);

    public override string BuildUpdateReturningSql(SqlBuildContext context)
        => BuildUpdateSql(context) + "; SELECT " + context.SelectColumns + " FROM " + context.Table + " WHERE " + context.KeyWhereClause;

    public override string BuildDeleteByKeySql(SqlBuildContext context)
        => "DELETE FROM " + context.Table + " WHERE " + AppendWhere(context.KeyWhereClause, context.ConcurrencyWhereClause);

    public override string BuildUpsertSql(SqlBuildContext context)
    {
        if (DatabaseMaySupplyKey(context))
        {
            return BuildGeneratedKeyUpsertSql(context);
        }

        return "INSERT INTO " + context.Table + " (" + context.InsertColumns + ") VALUES (" + context.InsertParameters + ") " +
            "ON DUPLICATE KEY UPDATE " + OnDuplicateKeyAssignments(context);
    }

    public override string BuildUpsertReturningSql(SqlBuildContext context)
    {
        if (DatabaseMaySupplyKey(context))
        {
            // KNOWN LIMITATION (follow-up: live MySQL integration): the trailing returning SELECT
            // keys off LAST_INSERT_ID(). On the INSERT branch that is the freshly generated key, but
            // on the ON DUPLICATE KEY UPDATE branch no auto-increment is generated, so
            // LAST_INSERT_ID() reflects the session's prior insert rather than the updated row — the
            // returned entity may be wrong or empty. Verify and fix (e.g. LAST_INSERT_ID(id) trick or
            // a key-based predicate) before relying on generated-key upsert-returning against a live
            // server. The non-generated-key path below is correct (keyed by @Key).
            return BuildGeneratedKeyUpsertSql(context) + "; " + BuildReturningSelect(context);
        }

        return BuildUpsertSql(context) + "; SELECT " + context.SelectColumns + " FROM " + context.Table + " WHERE " + context.KeyWhereClause;
    }

    private string BuildGeneratedKeyUpsertSql(SqlBuildContext context)
    {
        var keyColumn = context.QuotedKeyColumns[0];
        var keyParameter = context.KeyParameters[0];
        var explicitInsertColumns = JoinSql(keyColumn, context.InsertColumns);
        var explicitInsertParameters = JoinSql(keyParameter, context.InsertParameters);

        return "INSERT INTO " + context.Table + " (" + explicitInsertColumns + ") VALUES (" + explicitInsertParameters + ") " +
            "ON DUPLICATE KEY UPDATE " + OnDuplicateKeyAssignments(context);
    }

    /// <summary>
    /// Emulated-returning trailing <c>SELECT</c>. A single database-supplied key is read back via
    /// session-scoped <c>LAST_INSERT_ID()</c>; otherwise the row is selected by its key predicate.
    /// </summary>
    private string BuildReturningSelect(SqlBuildContext context)
    {
        var keyPredicate = DatabaseMaySupplyKey(context)
            ? context.QuotedKeyColumns[0] + " = LAST_INSERT_ID()"
            : context.KeyWhereClause;

        return "SELECT " + context.SelectColumns + " FROM " + context.Table + " WHERE " + keyPredicate;
    }

    // The MySQL-equivalent of SqlBuildContext.SetClauses, but assigning each column from the
    // attempted-insert value (VALUES(col)) rather than from a bound parameter — so the conflict
    // branch updates with the same data the insert branch would have written. VALUES(col) is
    // deprecated in MySQL 8.0.20+ but still functional and is the only form MariaDB / MySQL 5.7
    // understand, so it is chosen deliberately for cross-engine compatibility.
    private string OnDuplicateKeyAssignments(SqlBuildContext context)
        => string.Join(", ", context.Columns
            .Where(c => !c.IsKey && !c.IsGenerated)
            .Select(c =>
            {
                var quoted = QuoteIdentifier(c.ColumnName);
                return quoted + " = VALUES(" + quoted + ")";
            }));

    private static string JoinSql(string first, string rest)
        => string.IsNullOrEmpty(rest) ? first : first + ", " + rest;

    // ---- W7 DDL --------------------------------------------------------------------------------

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

    // MySQL cannot index a LONGTEXT column without a prefix length. A string column flagged
    // [InquiryColumn(IsIndexed)] without a Length (or explicit SqlType) maps to LONGTEXT, so its
    // CREATE INDEX would be rejected ("used in key specification without a key length"). Skip those
    // indexes rather than emit invalid DDL — the authoritative secondary-index contract is verified
    // against the hand-written schema, which bounds (or prefix-indexes) such columns.
    public override IReadOnlyList<string> BuildCreateIndexSql(SqlBuildContext context)
    {
        var statements = new List<string>();
        foreach (var column in context.Columns)
        {
            if (!column.IsIndexed && !column.IsUnique)
            {
                continue;
            }

            if (IsUnboundedString(column))
            {
                continue;
            }

            var indexName = string.IsNullOrEmpty(column.IndexName)
                ? (column.IsUnique ? "UX_" : "IX_") + context.RawTableName + "_" + column.ColumnName
                : column.IndexName!;
            var unique = column.IsUnique ? "UNIQUE " : string.Empty;
            statements.Add("CREATE " + unique + "INDEX " + QuoteIdentifier(indexName)
                + " ON " + context.Table + " (" + QuoteIdentifier(column.ColumnName) + ")");
        }

        return statements;
    }

    private static bool IsUnboundedString(IColumn column)
        => column.TypeClass == DbTypeClass.String
           && column.Length == 0
           && string.IsNullOrEmpty(column.SqlType);
}
