using System.Collections.Generic;
using System.Linq;

namespace Inquiry.Generators.Abstractions;

/// <summary>
/// Shared SQL builder for the MySQL family of engines (MySQL, MariaDB). Backtick identifier quoting,
/// <c>ON DUPLICATE KEY UPDATE</c> upsert, and — because MySQL does not support <c>RETURNING</c> on
/// DML — an emulated returning path: a two-statement batch ending in a <c>SELECT</c>. The trailing
/// <c>SELECT</c> is the first (and only) row-returning result set, so the existing pipeline (which
/// reads result set #1 under <c>CommandBehavior.SingleResult</c>) consumes it with zero runtime
/// changes. The MySQL and MariaDB dialect builders both derive from this class; engine-specific
/// divergence (such as MariaDB-native <c>RETURNING</c>) lands in the concrete subclass. JSON_TABLE
/// collection binding is shared here so both engines use one type mapping and extraction contract.
/// </summary>
public abstract class MySqlFamilySqlBuilder : SqlBuilder
{
    // lower_case_table_names is deployment-specific; ASCII-lower is the portable manifest envelope.
    public override string GetPhysicalIdentifierSortKey(string identifier) => FoldAscii(identifier, upper: false);
    public override bool ComputedColumnDeclaresStoreType => true;
    public override bool UseArrayInParameters => true;

    public override string ArrayParameterBinderFqn => "global::Inquiry.Parameters.InquiryJsonArrayParameter";

    protected override string RenderIn(string quotedColumn, string parameterName, DbTypeClass elementType)
    {
        var (columnType, valueExpression) = elementType switch
        {
            DbTypeClass.Boolean => ("BOOLEAN", "jt.val"),
            DbTypeClass.Byte => ("TINYINT UNSIGNED", "jt.val"),
            DbTypeClass.Int16 => ("SMALLINT", "jt.val"),
            DbTypeClass.Int32 => ("INT", "jt.val"),
            DbTypeClass.Int64 => ("BIGINT", "jt.val"),
            DbTypeClass.Single => ("FLOAT", "jt.val"),
            DbTypeClass.Double => ("DOUBLE", "jt.val"),
            DbTypeClass.Decimal => ("DECIMAL(65,30)", "jt.val"),
            DbTypeClass.DateTime => ("DATETIME(6)", "jt.val"),
            DbTypeClass.DateTimeOffset => ("VARCHAR(40)", "jt.val"),
            DbTypeClass.DateOnly => ("DATE", "jt.val"),
            DbTypeClass.TimeOnly => ("TIME(6)", "jt.val"),
            DbTypeClass.Guid => ("CHAR(36)", "jt.val"),
            DbTypeClass.ByteArray => ("LONGTEXT", "FROM_BASE64(jt.val)"),
            _ => ("LONGTEXT", "jt.val"),
        };

        return quotedColumn + " IN (SELECT " + valueExpression + " FROM JSON_TABLE(" + parameterName
            + ", '$[*]' COLUMNS(val " + columnType + " PATH '$')) jt)";
    }

    public override string QuoteIdentifier(string identifier)
        => "`" + identifier.Replace("`", "``") + "`";

    public override string BuildSelectByKeySql(SqlBuildContext context)
        => "SELECT " + context.SelectColumns + " FROM " + context.Table + " WHERE " + AppendWhere(context.KeyWhereClause, context.ActiveRowPredicate);

    public override string CurrentTimestampExpression => "UTC_TIMESTAMP(6)";

    public override bool SupportsFullTextSearch => true;

    /// <summary>MySQL-family bulk inserts ride MySqlBulkCopy via the provider-registered copier.</summary>
    public override bool SupportsBulkCopy => true;

    public override bool SupportsSetBasedBatchUpdate => true;

    public override string BuildSetBasedBatchUpdateHeader(string? schema, string tableName)
        => "UPDATE " + QuoteTable(schema, tableName) + " AS `_t` INNER JOIN (";

    public override string BuildSetBasedBatchUpdateFooter(
        string? schema,
        string tableName,
        IReadOnlyList<IColumn> keyColumns,
        IReadOnlyList<IColumn> setColumns,
        IReadOnlyList<string> writeEnforcedTerms)
    {
        var join = string.Join(" AND ", keyColumns.Select(column =>
            "`_t`." + QuoteIdentifier(column.ColumnName) + " = `_v`." + QuoteIdentifier(column.ColumnName)));
        var assignments = string.Join(", ", setColumns.Select(column =>
            "`_t`." + QuoteIdentifier(column.ColumnName) + " = `_v`." + QuoteIdentifier(column.ColumnName)));
        // Qualified with the target alias: the derived table `_v` selects the same column names, so an
        // unqualified filter term would be ambiguous — and matching `_v` would test the caller's own
        // payload rather than the stored row, enforcing nothing.
        var enforced = writeEnforcedTerms.Count == 0
            ? string.Empty
            : " WHERE " + string.Join(" AND ", writeEnforcedTerms.Select(static term => "`_t`." + term));
        return ") AS `_v` ON " + join + " SET " + assignments + enforced;
    }

    public override string BuildFullTextSearchSql(SqlBuildContext context, IReadOnlyList<IColumn> searchColumns)
    {
        // MATCH ... AGAINST natural-language search (requires a FULLTEXT index on the columns).
        var cols = string.Join(", ", searchColumns.Select(c => QuoteIdentifier(c.ColumnName)));
        var predicate = "MATCH(" + cols + ") AGAINST (" + ParameterName("searchTerm") + " IN NATURAL LANGUAGE MODE)";
        return "SELECT " + context.SelectColumns + " FROM " + context.Table + " WHERE " + AppendWhere(predicate, context.ActiveRowPredicate);
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
        if (HasCompositeDatabaseDefaultKey(context))
        {
            throw new System.NotSupportedException(
                "MySQL insert-returning cannot identify a row with a composite database-default key.");
        }

        if (HasNonAutoDatabaseDefaultKey(context))
        {
            return BuildDefaultKeyInsertReturningSql(context);
        }

        return BuildInsertSql(context) + "; " + BuildReturningSelect(context);
    }

    public override string BuildUpdateSql(SqlBuildContext context)
        => "UPDATE " + context.Table + " SET " + context.SetClausesWithVersion
            + " WHERE " + context.KeyWriteWhereClause;

    public override string BuildUpdateReturningSql(SqlBuildContext context)
    {
        // The emulated-returning SELECT re-reads the row AFTER the UPDATE, so it must not simply
        // re-test the write-enforced term: an update that legitimately CHANGES the filter column
        // (deactivating your own row, reassigning a tenant) would then find the post-update value
        // outside the predicate and return null for a write that succeeded. What actually proves the
        // UPDATE passed the enforced predicate is that it affected a row.
        //
        // Token entities get that proof from ROW_COUNT() > 0 alone — the version bump guarantees a
        // successful update changed a column, so the count is non-zero under either semantics below —
        // and the read-back is the plain key clause.
        //
        // Without a token, ROW_COUNT() alone is not safe to rely on: whether it counts rows MATCHED or
        // rows CHANGED depends on CLIENT_FOUND_ROWS, which callers control through the connection
        // string (MySqlConnector's UseAffectedRows). Under changed-row semantics a legitimate no-op
        // update reports 0 and the row would be lost. OR-ing the enforced term back in makes the
        // statement correct under both: the term is still true for an untouched row the caller owns,
        // and still false for another tenant's row — which fails BOTH operands, so nothing leaks.
        string returningPredicate;
        if (context.ConcurrencyToken is not null)
        {
            returningPredicate = AppendWhere(context.KeyWhereClause, "ROW_COUNT() > 0");
        }
        else if (context.WriteEnforcedPredicate.Length == 0)
        {
            returningPredicate = context.KeyWhereClause;
        }
        else
        {
            returningPredicate = AppendWhere(
                context.KeyWhereClause, "(ROW_COUNT() > 0 OR " + context.WriteEnforcedPredicate + ")");
        }

        return BuildUpdateSql(context) + "; SELECT " + context.SelectColumns + " FROM " + context.Table + " WHERE " + returningPredicate;
    }

    public override string BuildDeleteByKeySql(SqlBuildContext context)
        => "DELETE FROM " + context.Table + " WHERE " + context.KeyWriteWhereClause;

    public override string BuildUpsertSql(SqlBuildContext context)
    {
        if (HasNonAutoDatabaseDefaultKey(context))
        {
            return BuildDefaultKeyUpsertSql(context);
        }

        if (HasAutoIncrementKey(context))
        {
            return BuildGeneratedKeyUpsertSql(context);
        }

        return "INSERT INTO " + context.Table + " (" + context.InsertColumns + ") VALUES (" + context.InsertParameters + ") " +
            "ON DUPLICATE KEY UPDATE " + OnDuplicateKeyAssignments(context);
    }

    public override string BuildUpsertReturningSql(SqlBuildContext context)
    {
        if (HasCompositeDatabaseDefaultKey(context))
        {
            throw new System.NotSupportedException(
                "MySQL upsert-returning cannot identify a row with a composite database-default key.");
        }

        if (HasNonAutoDatabaseDefaultKey(context))
        {
            if (context.HasSecondaryUniqueConstraint)
            {
                throw new System.NotSupportedException(
                    "MySQL upsert-returning cannot identify the winning row after a secondary-unique conflict.");
            }

            return BuildDefaultKeyUpsertSql(context) + "; SELECT " + context.SelectColumns +
                " FROM " + context.Table + " WHERE " + context.KeyWhereClause;
        }

        if (HasAutoIncrementKey(context))
        {
            // Read the upserted row back by its key. An explicit non-zero key inserts (or, on conflict,
            // updates) that exact key, so select by @key directly. A 0/NULL key triggers AUTO_INCREMENT;
            // MySQL only sets LAST_INSERT_ID() when a value is actually generated (NOT for an explicit
            // non-null insert) and the ON DUPLICATE UPDATE branch never fires for a new row — so
            // LAST_INSERT_ID() is correct only for that auto-generated case. IF(@key, @key, LAST_INSERT_ID())
            // covers both without relying on echoing the key through the ON DUPLICATE branch.
            var keyColumn = context.QuotedKeyColumns[0];
            var keyParameter = context.KeyParameters[0];
            return BuildGeneratedKeyUpsertSql(context) + "; " +
                "SELECT " + context.SelectColumns + " FROM " + context.Table +
                " WHERE " + keyColumn + " = IF(" + keyParameter + ", " + keyParameter + ", LAST_INSERT_ID())";
        }

        return BuildUpsertSql(context) + "; SELECT " + context.SelectColumns + " FROM " + context.Table + " WHERE " + context.KeyWhereClause;
    }

    private string BuildGeneratedKeyUpsertSql(SqlBuildContext context)
    {
        var keyColumn = context.QuotedKeyColumns[0];
        var keyParameter = context.KeyParameters[0];
        var explicitInsertColumns = JoinSql(keyColumn, context.InsertColumns);
        var explicitInsertParameters = JoinSql(keyParameter, context.InsertParameters);

        // Append key = LAST_INSERT_ID(key) so the trailing SELECT can locate the row even when
        // ON DUPLICATE KEY fires on a secondary unique constraint (where LAST_INSERT_ID() is not
        // automatically set to the conflicting row's primary key).
        var withKey = keyColumn + " = LAST_INSERT_ID(" + keyColumn + ")";

        return "INSERT INTO " + context.Table + " (" + explicitInsertColumns + ") VALUES (" + explicitInsertParameters + ") " +
            "ON DUPLICATE KEY UPDATE " + OnDuplicateKeyAssignments(context, withKey);
    }

    /// <summary>
    /// Emulated-returning trailing <c>SELECT</c> for AUTO_INCREMENT and client-key paths. An
    /// AUTO_INCREMENT key is read back via session-scoped <c>LAST_INSERT_ID()</c>; otherwise the row
    /// is selected by its key predicate. Non-auto database-default keys branch to their capture path.
    /// </summary>
    private string BuildReturningSelect(SqlBuildContext context)
    {
        var keyPredicate = HasAutoIncrementKey(context)
            ? context.QuotedKeyColumns[0] + " = LAST_INSERT_ID()"
            : context.KeyWhereClause;

        return "SELECT " + context.SelectColumns + " FROM " + context.Table + " WHERE " + keyPredicate;
    }

    /// <summary>The collision-safe session variable holding a captured database-default key.</summary>
    private const string GeneratedDefaultKeyVariable = "@'__inquiry.generated-key'";

    /// <summary>
    /// True when the single key is database-generated by AUTO_INCREMENT.
    /// </summary>
    private static bool HasAutoIncrementKey(SqlBuildContext context)
        => context.KeyColumns.Count == 1 && context.KeyColumns[0].IsGenerated;

    private static bool HasNonAutoDatabaseDefaultKey(SqlBuildContext context)
        => context.KeyColumns.Count == 1 && !context.KeyColumns[0].IsGenerated && context.KeyColumns[0].UseDatabaseDefault;

    private static bool HasCompositeDatabaseDefaultKey(SqlBuildContext context)
        => context.KeyColumns.Count > 1 && context.KeyColumns.Any(static key => key.UseDatabaseDefault);

    // This SQL handles an explicit non-auto key. Nullable default-key upserts use ordinary INSERT SQL
    // when the key is null, allowing the database default to run.
    private string BuildDefaultKeyUpsertSql(SqlBuildContext context)
    {
        var keyColumn = context.QuotedKeyColumns[0];
        var insertColumns = JoinSql(keyColumn, context.InsertColumns);
        var insertValues = JoinSql(context.KeyParameters[0], context.InsertParameters);

        return "INSERT INTO " + context.Table + " (" + insertColumns + ") VALUES (" + insertValues + ") " +
            "ON DUPLICATE KEY UPDATE " + OnDuplicateKeyAssignments(context);
    }

    // MySQL must evaluate the deployed expression itself so INSERT and the trailing SELECT use the same
    // value. UUID() remains the backwards-compatible fallback for GUID keys.
    private string DefaultKeyCaptureExpression(SqlBuildContext context)
    {
        var key = context.KeyColumns[0];
        if (!string.IsNullOrWhiteSpace(key.DefaultExpression))
        {
            var mappedIdentifiers = context.Columns
                .SelectMany(static column => new[] { column.PropertyName, column.ColumnName })
                .Distinct(System.StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var failures = SqlExpressionLexer.ValidateStandaloneScalar(
                key.DefaultExpression!, ComputedExpressionCommentPolicy, mappedIdentifiers);
            if (failures.Count > 0)
            {
                throw new System.NotSupportedException(
                    "MySQL insert-returning DefaultExpression must be a standalone scalar: " +
                    string.Join("; ", failures));
            }

            return RenderDefaultExpression(key.DefaultExpression!);
        }

        if (key.TypeClass == DbTypeClass.Guid)
        {
            return "UUID()";
        }

        throw new System.NotSupportedException(
            "MySQL insert-returning for a non-auto database-default key requires DefaultExpression metadata.");
    }

    // SET evaluates the default once without deprecated user-variable expression assignment. The quoted
    // variable is reused by both INSERT and the emulated-returning SELECT.
    private string BuildDefaultKeyInsertReturningSql(SqlBuildContext context)
    {
        var keyColumn = context.QuotedKeyColumns[0];
        var insertColumns = JoinSql(keyColumn, context.InsertColumns);
        var insertValues = JoinSql(GeneratedDefaultKeyVariable, context.InsertParameters);
        var expression = DefaultKeyCaptureExpression(context);

        return "SET " + GeneratedDefaultKeyVariable + " = " + expression + "; " +
            "INSERT INTO " + context.Table + " (" + insertColumns + ") VALUES (" + insertValues + "); " +
            "SELECT " + context.SelectColumns + " FROM " + context.Table + " WHERE " + keyColumn + " = " + GeneratedDefaultKeyVariable;
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
    private string OnDuplicateKeyAssignments(SqlBuildContext context, string? requiredKeyAssignment = null)
    {
        var assignments = string.Join(", ", context.Columns
            .Where(c => !c.IsKey && !c.IsGenerated && !c.IsConcurrencyToken && !c.IsCreatedAt && !c.IsCreatedBy && string.IsNullOrEmpty(c.ComputedExpression))
            .Select(c =>
            {
                var quoted = QuoteIdentifier(c.ColumnName);
                return c.UseDatabaseDefault
                    ? quoted + " = " + ParameterName(c.PropertyName)
                    : quoted + " = VALUES(" + quoted + ")";
            }));

        if (!string.IsNullOrEmpty(context.ConcurrencyVersionSet))
        {
            assignments = string.IsNullOrEmpty(assignments)
                ? context.ConcurrencyVersionSet
                : assignments + ", " + context.ConcurrencyVersionSet;
        }

        if (!string.IsNullOrEmpty(requiredKeyAssignment))
        {
            assignments = string.IsNullOrEmpty(assignments)
                ? requiredKeyAssignment!
                : assignments + ", " + requiredKeyAssignment!;
        }

        if (assignments.Length == 0)
        {
            // An entity whose only columns are keys (no updatable non-key columns) produces an
            // empty SET. MySQL requires at least one assignment after ON DUPLICATE KEY UPDATE, so
            // emit a no-op `key = key` that satisfies the parser without modifying data.
            var key = context.QuotedKeyColumns[0];
            return key + " = " + key;
        }

        return assignments;
    }

    private static string JoinSql(string first, string rest)
        => string.IsNullOrEmpty(rest) ? first : first + ", " + rest;

    // ---- DDL --------------------------------------------------------------------------------

    // MySQL cannot index LONGTEXT without a prefix length; a string key needs an explicit Length.
    public override bool RequiresBoundedStringKeys => true;

    // MySQL's single-column VARCHAR maxes at 65,535 bytes; under utf8mb4 (4 bytes/char) that is ~16,383
    // chars. A longer declared Length maps to LONGTEXT (see MapColumnType) rather than an illegal
    // VARCHAR(>16383), and cannot be keyed/indexed without a prefix length.
    protected internal override int MaxBoundedStringLength(bool isUnicode) => 16383;

    // MySQL/InnoDB auto-creates a backing index for every foreign-key column, so the INQ061
    // unindexed-FK lint does not apply.
    public override bool ForeignKeysAreAutoIndexed => true;

    /// <summary>MySQL-family computed columns are typed and STORED.</summary>
    protected override string RenderComputedColumn(IColumn column)
        => ColumnType(column) + " GENERATED ALWAYS AS (" + column.ComputedExpression + ") STORED";

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
        DbTypeClass.DateTime or DbTypeClass.DateTimeOffset => "DATETIME(6)",
        DbTypeClass.DateOnly => "DATE",
        // TIME(6) keeps the microsecond precision a TimeOnly carries (plain TIME truncates to seconds).
        DbTypeClass.TimeOnly => "TIME(6)",
        DbTypeClass.Guid => "CHAR(36)",
        DbTypeClass.ByteArray => "LONGBLOB",
        // MySQL cannot index LONGTEXT; a bounded Length is required for PK/FK string columns. A Length over
        // the VARCHAR ceiling (MaxBoundedStringLength) falls back to LONGTEXT rather than illegal DDL.
        _ => column.Length > 0 && column.Length <= MaxBoundedStringLength(column.IsUnicode)
            ? "VARCHAR(" + column.Length + ")"
            : "LONGTEXT",
    };

    protected override string GeneratedKeyClause(IColumn column)
        => MapColumnType(column) + " AUTO_INCREMENT PRIMARY KEY";

    // MySQL extracts a JSON scalar with JSON_EXTRACT, unquoted (JSON_UNQUOTE) so it compares as text
    // rather than a JSON-quoted string.
    protected override string RenderJsonPathExtract(string quotedColumn, string jsonPath)
        => "JSON_UNQUOTE(JSON_EXTRACT(" + quotedColumn + ", '" + jsonPath + "'))";
}
