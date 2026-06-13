using System.Collections.Generic;
using Inquiry.Generators.Infrastructure;
using Inquiry.Generators.Models;
using Microsoft.CodeAnalysis;

namespace Inquiry.Generators.Abstractions;

/// <summary>
/// Compile-time SQL builder consumed by the Inquiry source generator. One concrete subclass exists
/// per supported dialect, lives in that provider's analyzer assembly, and is registered with
/// <see cref="SqlBuilderRegistry"/> at analyzer load time. The Inquiry runtime ships no SQL — every
/// statement is produced here and emitted as a <c>const string</c> field at compile time.
/// </summary>
/// <remarks>
/// FOUNDATION CONVENTION: when a feature workstream adds a new capability, prefer a
/// <c>virtual</c> method with a base-class default implementation wherever the SQL is dialect-uniform,
/// so adding the capability does not force an edit in every provider subclass. Use <c>abstract</c>
/// only when the SQL genuinely has no portable default. All WHERE-clause shaping (key, filter,
/// concurrency token, soft-delete) MUST compose through <see cref="AppendWhere"/> so AND-joining is
/// implemented once rather than duplicated (and divergently) across providers.
/// </remarks>
public abstract class SqlBuilder
{
    public abstract string DialectName { get; }

    public virtual string ParameterName(string logicalName) => "@" + logicalName;

    /// <summary>
    /// The fully-qualified <c>System.Data.DbType</c> expression bound onto a generated parameter for a
    /// column of the given <paramref name="type"/>, or <c>null</c> when no portable DbType applies (the
    /// provider then infers it). Routes <see cref="System.DateTime"/> through
    /// <see cref="DateTimeDbTypeExpression"/> — the one mapping that varies by dialect — and delegates
    /// everything else to the portable <see cref="DbTypeMapper"/>.
    /// </summary>
    internal string? MapDbTypeExpression(TypeData type)
        => type.SpecialType == SpecialType.System_DateTime
            ? DateTimeDbTypeExpression
            : DbTypeMapper.TryGetDbTypeExpression(type);

    /// <summary>
    /// As <see cref="MapDbTypeExpression(TypeData)"/> but for a value converter's provider
    /// <see cref="SpecialType"/>; the same dialect substitution for <see cref="System.DateTime"/>
    /// applies.
    /// </summary>
    internal string? MapDbTypeExpressionForSpecialType(SpecialType specialType)
        => specialType == SpecialType.System_DateTime
            ? DateTimeDbTypeExpression
            : DbTypeMapper.TryGetDbTypeForSpecialType(specialType);

    /// <summary>
    /// The DbType expression emitted for a <see cref="System.DateTime"/> parameter. Default
    /// <c>DbType.DateTime2</c> (SqlClient maps <c>DbType.DateTime</c> to the legacy <c>datetime</c> type,
    /// which can truncate against <c>datetime2</c> columns; Npgsql/SQLite/MySQL treat the two
    /// equivalently). Oracle overrides this with <c>DbType.DateTime</c> because ODP.NET's
    /// <c>OracleParameter</c> rejects <c>DbType.DateTime2</c> ("Value does not fall within the expected
    /// range").
    /// </summary>
    public virtual string DateTimeDbTypeExpression => "global::System.Data.DbType.DateTime2";

    // ---- Batch insert / update ---------------------------------------------------------

    /// <summary>
    /// Header of a multi-row batch <c>INSERT</c> — the <c>_sqlInsertAllPrefix</c> const emitted before the
    /// per-row value tuples. Default is the standard multi-row form <c>INSERT INTO t (cols) VALUES </c>.
    /// Oracle overrides with <c>INSERT ALL </c> (its multi-row insert repeats <c>INTO t (cols) VALUES (…)</c>
    /// per row and ends with a <c>SELECT … FROM dual</c>).
    /// </summary>
    public virtual string BuildBatchInsertHeader(SqlBuildContext context)
        => "INSERT INTO " + context.Table + " (" + context.InsertColumns + ") VALUES ";

    /// <summary>
    /// Text opening one row's value tuple, before its bound parameters. Default <c>(</c>; Oracle repeats
    /// <c>INTO t (cols) VALUES (</c> per row.
    /// </summary>
    public virtual string BuildBatchInsertRowOpen(SqlBuildContext context) => "(";

    /// <summary>Separator placed between row tuples. Default <c>,</c> (multi-row VALUES); Oracle uses a space.</summary>
    public virtual string BatchInsertRowSeparator => ",";

    /// <summary>Trailing text after all row tuples. Default empty; Oracle appends <c> SELECT 1 FROM dual</c>.</summary>
    public virtual string BatchInsertFooter => "";

    public string QuoteTable(string? schema, string tableName)
    {
        return string.IsNullOrEmpty(schema)
            ? QuoteIdentifier(tableName)
            : QuoteIdentifier(schema!) + "." + QuoteIdentifier(tableName);
    }

    public abstract string QuoteIdentifier(string identifier);

    public abstract string BuildSelectAllSql(SqlBuildContext context);

    public abstract string BuildSelectByKeySql(SqlBuildContext context);

    public abstract string BuildSelectByFieldSql(SqlBuildContext context, IReadOnlyList<IColumn> filterColumns);

    /// <summary>
    /// Builds the single-parent many-to-many eager-load SELECT: the related (child) rows joined through a
    /// junction table, filtered by the junction's parent foreign key. Dialect-uniform — child columns are
    /// qualified with the child table (not an alias) to stay unambiguous against the junction, and the
    /// junction takes a space alias (no <c>AS</c>, which Oracle rejects for table aliases). All names are
    /// quoted through <see cref="QuoteIdentifier"/> / <see cref="QuoteTable"/>.
    /// </summary>
    public virtual string BuildManyToManySelectByParentSql(
        SqlBuildContext childContext,
        IReadOnlyList<IColumn> childColumns,
        string? junctionSchema,
        string junctionTable,
        string junctionChildForeignKeyColumn,
        string childKeyColumn,
        string junctionParentForeignKeyColumn,
        string parentParameterName)
    {
        const string j = "__j";
        var junctionQuoted = QuoteTable(junctionSchema, junctionTable);
        var childCols = new System.Text.StringBuilder();
        for (var i = 0; i < childColumns.Count; i++)
        {
            if (i > 0)
            {
                childCols.Append(", ");
            }

            childCols.Append(childContext.Table).Append('.').Append(QuoteIdentifier(childColumns[i].ColumnName));
        }

        return "SELECT " + childCols.ToString()
            + " FROM " + childContext.Table
            + " INNER JOIN " + junctionQuoted + " " + j
            + " ON " + j + "." + QuoteIdentifier(junctionChildForeignKeyColumn) + " = " + childContext.Table + "." + QuoteIdentifier(childKeyColumn)
            + " WHERE " + j + "." + QuoteIdentifier(junctionParentForeignKeyColumn) + " = " + ParameterName(parentParameterName);
    }

    /// <summary>
    /// Builds a SELECT whose WHERE clause is the AND/OR composition of <paramref name="predicates"/>.
    /// Dialect-uniform: the base implementation renders every operator portably (comparison, BETWEEN,
    /// IS [NOT] NULL, plus the <see cref="RenderLike"/>/<see cref="RenderIn"/> hooks). Providers only
    /// override a hook when their LIKE/IN syntax differs. The composed predicate body is routed through
    /// <see cref="AppendWhere"/> so it stays consistent with key/field WHERE shaping.
    /// </summary>
    public virtual string BuildSelectByPredicateSql(SqlBuildContext context, IReadOnlyList<SqlPredicate> predicates)
        => "SELECT " + context.SelectColumns + " FROM " + context.Table
            + " WHERE " + AppendWhere(RenderPredicates(predicates), context.ActiveRowPredicate);

    // ---- Set-based predicate mutations ([InquiryUpdateWhere] / [InquiryDeleteWhere]) ----

    /// <summary>
    /// Builds a set-based UPDATE whose WHERE clause is the AND/OR composition of
    /// <paramref name="predicates"/> — the ExecuteUpdate analog. SET parameters take the same
    /// <c>@{PropertyName}</c> scheme as the single-row UPDATE; predicate parameter names are
    /// uniquified against the SET names by the generator (a column both assigned and filtered binds
    /// its filter as <c>@{PropertyName}2</c>), so the two namespaces cannot collide. A soft-delete
    /// entity AND-composes the active-row filter, exactly like predicate SELECTs — a set-based
    /// update never touches soft-deleted rows. Dialect-uniform (quoting, sigils, and operator
    /// rendering all route through the shared hooks), so concrete and inherited by every provider.
    /// </summary>
    public virtual string BuildUpdateByPredicateSql(SqlBuildContext context, IReadOnlyList<IColumn> setColumns, IReadOnlyList<SqlPredicate> predicates)
    {
        var set = new System.Text.StringBuilder();
        for (var i = 0; i < setColumns.Count; i++)
        {
            if (i > 0)
            {
                set.Append(", ");
            }

            set.Append(QuoteIdentifier(setColumns[i].ColumnName)).Append(" = ").Append(ParameterName(setColumns[i].PropertyName));
        }

        return "UPDATE " + context.Table + " SET " + set.ToString()
            + " WHERE " + AppendWhere(RenderPredicates(predicates), context.ActiveRowPredicate);
    }

    /// <summary>
    /// Builds a set-based literal DELETE whose WHERE clause is the AND/OR composition of
    /// <paramref name="predicates"/> — the ExecuteDelete analog. Used for entities without a
    /// soft-delete column, or with <c>HardDelete = true</c> (mirroring the by-key delete, no
    /// active-row filter is composed: a hard delete may remove already-soft-deleted rows too).
    /// </summary>
    public virtual string BuildDeleteByPredicateSql(SqlBuildContext context, IReadOnlyList<SqlPredicate> predicates)
        => "DELETE FROM " + context.Table + " WHERE " + RenderPredicates(predicates);

    /// <summary>
    /// The soft form of <see cref="BuildDeleteByPredicateSql"/>: an UPDATE that sets the soft-delete
    /// indicator (the same SET shape as <see cref="BuildSoftDeleteByKeySql"/>) on every matching row.
    /// AND-composes the active-row filter — soft-deleting an already-deleted row is a no-op, and
    /// excluding those rows keeps the returned rows-affected count meaningful.
    /// </summary>
    public virtual string BuildSoftDeleteByPredicateSql(SqlBuildContext context, IReadOnlyList<SqlPredicate> predicates)
        => "UPDATE " + context.Table + " SET " + context.SoftDeleteSetClause
            + " WHERE " + AppendWhere(RenderPredicates(predicates), context.ActiveRowPredicate);

    public abstract string BuildInsertSql(SqlBuildContext context);

    public abstract string BuildInsertReturningSql(SqlBuildContext context);

    public abstract string BuildUpdateSql(SqlBuildContext context);

    public abstract string BuildUpdateReturningSql(SqlBuildContext context);

    public abstract string BuildDeleteByKeySql(SqlBuildContext context);

    // ---- Soft delete -------------------------------------------------------------------

    /// <summary>
    /// SQL literal for a boolean <c>false</c> (soft-delete "active" flag, global-filter false condition).
    /// Default <c>0</c> (SQLite/SqlServer/MySQL); PostgreSQL overrides with <c>FALSE</c>.
    /// </summary>
    public virtual string BooleanFalseLiteral => "0";

    /// <summary>
    /// SQL literal for a boolean <c>true</c> (soft-delete "deleted" flag, global-filter true condition).
    /// Default <c>1</c> (SQLite/SqlServer/MySQL); PostgreSQL overrides with <c>TRUE</c>.
    /// </summary>
    public virtual string BooleanTrueLiteral => "1";

    /// <summary>
    /// SQL expression yielding the database clock used to stamp a timestamp-form soft delete. Default
    /// <c>CURRENT_TIMESTAMP</c> (SQLite/PostgreSQL/MySQL); SqlServer overrides with <c>GETUTCDATE()</c>.
    /// </summary>
    public virtual string CurrentTimestampExpression => "CURRENT_TIMESTAMP";

    /// <summary>
    /// Builds the soft-delete UPDATE (set the indicator to deleted) by key. Dialect-uniform once the
    /// indicator literals are abstracted, so this is concrete and every provider inherits it. Only
    /// emitted when the entity has a soft-delete column; callers pick this over
    /// <see cref="BuildDeleteByKeySql"/> for a non-hard delete.
    /// </summary>
    public virtual string BuildSoftDeleteByKeySql(SqlBuildContext context)
        => "UPDATE " + context.Table + " SET " + context.SoftDeleteSetClause
            + " WHERE " + AppendWhere(context.KeyWhereClause, context.ConcurrencyWhereClause);

    /// <summary>
    /// Builds the restore UPDATE (clear the soft-delete indicator) by key. Concrete and inherited by
    /// every provider, mirroring <see cref="BuildSoftDeleteByKeySql"/>.
    /// </summary>
    public virtual string BuildRestoreByKeySql(SqlBuildContext context)
        => "UPDATE " + context.Table + " SET " + context.SoftDeleteRestoreSetClause + " WHERE " + context.KeyWhereClause;

    // ---- Batch delete by key collection -----------------------------------------------

    /// <summary>
    /// Builds a batch delete over a collection of single-column keys —
    /// <c>DELETE FROM t WHERE "Key" IN (:keys)</c>. The <c>(keys)</c> sentinel takes the dialect sigil via
    /// <see cref="ParameterName"/> (<c>:keys</c> on Oracle, <c>@keys</c> elsewhere) and is expanded at runtime
    /// by <c>InquiryInExpansion</c> into one placeholder per element; the emitter passes the same dialect name.
    /// Dialect-uniform (single key guaranteed by validation), so concrete and inherited by every provider.
    /// </summary>
    public virtual string BuildDeleteAllByKeysSql(SqlBuildContext context)
        => "DELETE FROM " + context.Table + " WHERE " + RenderIn(context.QuotedKeyColumns[0], ParameterName("keys"));

    /// <summary>
    /// The soft-delete form of <see cref="BuildDeleteAllByKeysSql"/> — sets the soft-delete indicator
    /// on every row whose key is in the collection instead of physically removing it.
    /// </summary>
    public virtual string BuildSoftDeleteAllByKeysSql(SqlBuildContext context)
        => "UPDATE " + context.Table + " SET " + context.SoftDeleteSetClause + " WHERE " + RenderIn(context.QuotedKeyColumns[0], ParameterName("keys"));

    public abstract string BuildUpsertSql(SqlBuildContext context);

    public abstract string BuildUpsertReturningSql(SqlBuildContext context);

    /// <summary>
    /// Builds a <c>SELECT COUNT(*)</c> over the entity's table. Dialect-uniform (ANSI), so this is
    /// concrete and inherited by every provider; it composes the soft-delete active filter via
    /// <see cref="WhereSuffix"/> so a count excludes soft-deleted rows when applicable.
    /// </summary>
    public virtual string BuildCountSql(SqlBuildContext context)
        => "SELECT COUNT(*) FROM " + context.Table + WhereSuffix(context.ActiveRowPredicate);

    /// <summary>
    /// Builds a scalar aggregate (<c>SELECT SUM("col") FROM …</c>). <paramref name="function"/> is the
    /// ANSI function name (SUM/AVG/MIN/MAX) and <paramref name="quotedColumn"/> is already dialect-quoted.
    /// Dialect-uniform, so concrete and inherited; composes the soft-delete active filter.
    /// </summary>
    public virtual string BuildAggregateSql(SqlBuildContext context, string function, string quotedColumn)
        => "SELECT " + function + "(" + quotedColumn + ") FROM " + context.Table + WhereSuffix(context.ActiveRowPredicate);

    /// <summary>
    /// Whether this dialect supports <c>[InquiryFullTextSearch]</c>. Default <see langword="false"/>
    /// (SQLite/Oracle in v1); PostgreSQL, SQL Server, and MySQL override to <see langword="true"/>.
    /// </summary>
    public virtual bool SupportsFullTextSearch => false;

    /// <summary>
    /// Builds a full-text search SELECT over <paramref name="searchColumns"/>, bound to a single
    /// <c>@searchTerm</c> parameter. Composes the soft-delete active filter. Supporting dialects
    /// override this; the base throws so an unsupported dialect is caught at generation time.
    /// </summary>
    public virtual string BuildFullTextSearchSql(SqlBuildContext context, IReadOnlyList<IColumn> searchColumns)
        => throw new System.NotSupportedException(DialectName + " does not support full-text search.");

    /// <summary>
    /// Builds the ORDER BY clause body (no leading space) for the resolved terms, e.g.
    /// <c>ORDER BY "Name" ASC, "Id" DESC</c>. Dialect-uniform, so this is the single implementation all
    /// providers inherit. Returns the empty string when there are no terms.
    /// </summary>
    public virtual string BuildOrderByClause(SqlSelectOptions options)
    {
        if (options.OrderBy.Count == 0)
        {
            return string.Empty;
        }

        var sb = new System.Text.StringBuilder("ORDER BY ");
        for (var i = 0; i < options.OrderBy.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(", ");
            }

            var term = options.OrderBy[i];
            sb.Append(term.QuotedColumn);
            sb.Append(term.Descending ? " DESC" : " ASC");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Builds the offset-pagination tail (no leading space). The portable default is the
    /// <c>LIMIT @limit OFFSET @offset</c> form used by SQLite, PostgreSQL, and MySQL; SQL Server (and
    /// Oracle) override with the <c>OFFSET … FETCH</c> form, which requires a preceding ORDER BY.
    /// </summary>
    public virtual string BuildPaginationClause(SqlSelectOptions options)
        => "LIMIT " + options.LimitParameter + " OFFSET " + options.OffsetParameter;

    /// <summary>
    /// Builds the keyset <b>seek</b> comparison predicate body (no leading <c>WHERE</c>) for a non-null
    /// cursor — a plain, sargable <c>key &gt; @cursor</c> the engine can satisfy with an index seek. The
    /// portable default uses a row-value comparison <c>(a, b) &gt; (@c0, @c1)</c>; SQL Server, which lacks
    /// row-value <c>&gt;</c>, overrides with the lexicographic OR-form. Single-column keysets use a plain
    /// scalar comparison in both.
    /// </summary>
    /// <remarks>
    /// There is deliberately no <c>(@cursor IS NULL OR …)</c> guard here: that disjunction is non-sargable
    /// (the planner cannot prove a range when the cursor might be null), so it forces a full table scan
    /// instead of an index seek — keyset paging then degrades to O(table size). The null-cursor (first
    /// page) case is served by a separate predicate-free query the generator emits alongside this one.
    /// </remarks>
    public virtual string BuildKeysetPredicate(SqlSelectOptions options)
    {
        var op = options.KeysetDescending ? " < " : " > ";

        if (options.KeysetColumns.Count == 1)
        {
            return options.KeysetColumns[0] + op + options.KeysetCursorParameters[0];
        }

        var columns = "(" + string.Join(", ", options.KeysetColumns) + ")";
        var cursors = "(" + string.Join(", ", options.KeysetCursorParameters) + ")";
        return columns + op + cursors;
    }

    // ---- schema DDL generation -----------------------------------------------------------

    /// <summary>
    /// Builds the <c>CREATE TABLE</c> DDL for the entity described by <paramref name="context"/>.
    /// Dialect-uniform skeleton (column list, primary key, foreign keys) composed from the per-dialect
    /// hooks <see cref="MapColumnType"/>, <see cref="GeneratedKeyClause"/>, and <see cref="WrapCreateTable"/>:
    /// <list type="bullet">
    /// <item>a single generated key is emitted via <see cref="GeneratedKeyClause"/> (inline identity + PK);</item>
    /// <item>a single non-generated key gets an inline <c>PRIMARY KEY</c>;</item>
    /// <item>a composite key gets a table-level <c>PRIMARY KEY (…)</c> constraint;</item>
    /// <item>foreign keys become table-level <c>FOREIGN KEY … REFERENCES …</c> when the entity opts in.</item>
    /// </list>
    /// </summary>
    public virtual string BuildCreateTableSql(SqlBuildContext context)
    {
        var keyColumns = context.KeyColumns;
        var singleGeneratedKey = keyColumns.Count == 1 && keyColumns[0].IsGenerated;
        var compositeKey = keyColumns.Count > 1;

        var lines = new List<string>();
        foreach (var column in context.Columns)
        {
            if (singleGeneratedKey && column.IsKey)
            {
                lines.Add(QuoteIdentifier(column.ColumnName) + " " + GeneratedKeyClause(column));
                continue;
            }

            // A server-computed column's whole definition is delegated to RenderComputedColumn (which
            // itself includes a type on PostgreSQL/MySQL); it skips the normal type/NOT NULL/DEFAULT/
            // PRIMARY KEY handling below, since the database owns its value.
            if (!string.IsNullOrEmpty(column.ComputedExpression))
            {
                lines.Add(QuoteIdentifier(column.ColumnName) + " " + RenderComputedColumn(column));
                continue;
            }

            var def = QuoteIdentifier(column.ColumnName) + " " + ColumnType(column);
            if (!compositeKey && column.IsKey)
            {
                def += " PRIMARY KEY";
            }

            if (!column.IsNullable)
            {
                def += " NOT NULL";
            }

            if (!string.IsNullOrEmpty(column.DefaultExpression))
            {
                def += " DEFAULT " + column.DefaultExpression;
            }

            lines.Add(def);
        }

        if (compositeKey)
        {
            lines.Add("PRIMARY KEY (" + string.Join(", ", context.QuotedKeyColumns) + ")");
        }

        if (context.GenerateForeignKeys)
        {
            foreach (var column in context.Columns)
            {
                if (string.IsNullOrEmpty(column.ForeignKeyTable) || string.IsNullOrEmpty(column.ForeignKeyColumn))
                {
                    continue;
                }

                lines.Add("FOREIGN KEY (" + QuoteIdentifier(column.ColumnName) + ") REFERENCES "
                    + QuoteTable(column.ForeignKeySchema, column.ForeignKeyTable!) + "(" + QuoteIdentifier(column.ForeignKeyColumn!) + ")");
            }
        }

        return WrapCreateTable(context, string.Join(",\n    ", lines));
    }

    /// <summary>
    /// Builds the <c>CREATE INDEX</c> statements for the entity — one per column flagged
    /// <see cref="IColumn.IsIndexed"/> or <see cref="IColumn.IsUnique"/>. The index name defaults to
    /// <c>IX_&lt;table&gt;_&lt;column&gt;</c> (<c>UX_</c> for unique). Dialect-uniform apart from the
    /// idempotency guard, which is gated by <see cref="SupportsCreateIndexIfNotExists"/>.
    /// </summary>
    public virtual IReadOnlyList<string> BuildCreateIndexSql(SqlBuildContext context)
    {
        var statements = new List<string>();
        foreach (var column in context.Columns)
        {
            if (!column.IsIndexed && !column.IsUnique)
            {
                continue;
            }

            // A bounded-key dialect (Oracle / SQL Server / MySQL) cannot index an unbounded string: it
            // maps to a LOB/MAX text type (CLOB / NVARCHAR(MAX) / LONGTEXT) the engine rejects as an index
            // key. Skip the index rather than emit invalid DDL — bound the column with
            // [InquiryColumn(Length = …)] to have it indexed (the generator also reports INQ032).
            // SQLite/PostgreSQL index unbounded TEXT fine (RequiresBoundedStringKeys is false), so they
            // keep the index.
            if (RequiresBoundedStringKeys && IsUnboundedString(column))
            {
                continue;
            }

            var indexName = string.IsNullOrEmpty(column.IndexName)
                ? (column.IsUnique ? "UX_" : "IX_") + context.RawTableName + "_" + column.ColumnName
                : column.IndexName!;
            var unique = column.IsUnique ? "UNIQUE " : string.Empty;
            var guard = SupportsCreateIndexIfNotExists ? "IF NOT EXISTS " : string.Empty;
            statements.Add("CREATE " + unique + "INDEX " + guard + QuoteIdentifier(indexName)
                + " ON " + context.Table + " (" + QuoteIdentifier(column.ColumnName) + ")");
        }

        return statements;
    }

    /// <summary>
    /// Whether <c>CREATE INDEX IF NOT EXISTS</c> is supported (SQLite/PostgreSQL). False for SQL
    /// Server, MySQL, and Oracle, whose <c>CREATE INDEX</c> has no portable existence guard — on those
    /// dialects the emitted index DDL is therefore run-once (re-running the schema fails on the index),
    /// matching Oracle's already non-idempotent <c>CREATE TABLE</c>. Documented on <c>[InquiryColumn]</c>.
    /// </summary>
    protected virtual bool SupportsCreateIndexIfNotExists => false;

    /// <summary>
    /// True for a string column that maps to the dialect's unbounded text type — no explicit
    /// <see cref="IColumn.Length"/> and no <see cref="IColumn.SqlType"/> override (TEXT / CLOB /
    /// NVARCHAR(MAX) / LONGTEXT). On a bounded-key dialect such a column cannot be a key or index target.
    /// </summary>
    protected static bool IsUnboundedString(IColumn column)
        => column.TypeClass == DbTypeClass.String
           && column.Length == 0
           && string.IsNullOrEmpty(column.SqlType);

    /// <summary>The physical column type: the explicit <see cref="IColumn.SqlType"/> override if set, else <see cref="MapColumnType"/>.</summary>
    protected string ColumnType(IColumn column)
        => string.IsNullOrEmpty(column.SqlType) ? MapColumnType(column) : column.SqlType!;

    /// <summary>
    /// Renders a server-computed column's definition (the part after the quoted name) for a
    /// <c>[InquiryColumn(Computed = …)]</c> column. The default is the standard expression form
    /// <c>AS (&lt;expr&gt;)</c> (SQLite / SQL Server / Oracle); PostgreSQL and MySQL override to the
    /// typed <c>GENERATED ALWAYS AS (&lt;expr&gt;) STORED</c> form they require.
    /// </summary>
    protected virtual string RenderComputedColumn(IColumn column)
        => "AS (" + column.ComputedExpression + ")";

    /// <summary>
    /// Renders the <c>precision, scale</c> body for a decimal column type, using the column's declared
    /// <see cref="IColumn.Precision"/>/<see cref="IColumn.Scale"/> when set, else the dialect defaults.
    /// </summary>
    protected static string DecimalSpec(IColumn column, int defaultPrecision, int defaultScale)
        => column.Precision > 0
            ? column.Precision + ", " + column.Scale
            : defaultPrecision + ", " + defaultScale;

    /// <summary>
    /// Whether this dialect rejects a primary key over an unbounded text column (so a string key
    /// needs an explicit <see cref="IColumn.Length"/>). False for SQLite/PostgreSQL (unbounded TEXT keys
    /// are allowed); SQL Server, MySQL, and Oracle override to true.
    /// </summary>
    public virtual bool RequiresBoundedStringKeys => false;

    /// <summary>
    /// Whether the dialect automatically creates a backing index for a foreign-key column when its
    /// constraint is generated. MySQL/InnoDB does; the others do not, so an un-indexed FK column there
    /// gets the INQ061 lint. Default false; MySQL overrides to true.
    /// </summary>
    public virtual bool ForeignKeysAreAutoIndexed => false;

    /// <summary>
    /// Maps a column's dialect-neutral <see cref="IColumn.TypeClass"/> (plus length/precision/scale)
    /// to a physical column type for this dialect. No leading column name. Abstract so every provider
    /// supplies its own type table — adding a dialect forces an explicit mapping rather than a silent default.
    /// </summary>
    protected abstract string MapColumnType(IColumn column);

    /// <summary>
    /// The full column definition (after the quoted name) for a single database-generated primary key,
    /// e.g. <c>INTEGER PRIMARY KEY AUTOINCREMENT</c> / <c>INT IDENTITY(1,1) PRIMARY KEY</c> / <c>SERIAL PRIMARY KEY</c>.
    /// </summary>
    protected abstract string GeneratedKeyClause(IColumn column);

    /// <summary>
    /// Wraps the comma-separated column/constraint <paramref name="body"/> in the dialect's
    /// <c>CREATE TABLE</c> statement. Default is the idempotent <c>CREATE TABLE IF NOT EXISTS</c> form
    /// (SQLite/PostgreSQL/MySQL); SQL Server wraps in an <c>OBJECT_ID</c> guard and Oracle omits the guard.
    /// </summary>
    protected virtual string WrapCreateTable(SqlBuildContext context, string body)
        => "CREATE TABLE IF NOT EXISTS " + context.Table + " (\n    " + body + "\n)";

    protected static bool DatabaseMaySupplyKey(SqlBuildContext context)
    {
        if (context.KeyColumns.Count != 1) return false;
        var key = context.KeyColumns[0];
        return key.IsGenerated || key.UseDatabaseDefault;
    }

    /// <summary>
    /// Composes WHERE-clause predicate bodies. Returns <paramref name="whereClause"/> unchanged when
    /// <paramref name="extraPredicate"/> is null/empty, the extra predicate alone when the existing
    /// clause is null/empty, otherwise both AND-joined. The returned string is a predicate body with
    /// no leading <c>WHERE</c> keyword — callers prepend <c>" WHERE "</c> only when the result is
    /// non-empty. This is the single composition point every WHERE-shaping feature funnels through.
    /// </summary>
    protected static string AppendWhere(string whereClause, string? extraPredicate)
    {
        if (string.IsNullOrEmpty(extraPredicate))
        {
            return whereClause;
        }

        return string.IsNullOrEmpty(whereClause)
            ? extraPredicate!
            : GroupForAndComposition(whereClause) + " AND " + extraPredicate;
    }

    private static string GroupForAndComposition(string whereClause)
        => whereClause.IndexOf(" OR ", System.StringComparison.OrdinalIgnoreCase) >= 0
            ? "(" + whereClause + ")"
            : whereClause;

    /// <summary>
    /// Renders a leading <c>" WHERE &lt;body&gt;"</c> suffix, or the empty string when
    /// <paramref name="predicateBody"/> is null/empty. Used by <c>SELECT *</c>-style statements (no key/
    /// field WHERE of their own) so they pick up a WHERE only when the soft-delete filter is active.
    /// </summary>
    protected static string WhereSuffix(string? predicateBody)
        => string.IsNullOrEmpty(predicateBody) ? string.Empty : " WHERE " + predicateBody;

    /// <summary>
    /// Renders the predicate body (no leading <c>WHERE</c>) by joining each criterion with AND, or OR
    /// when <see cref="SqlPredicate.IsOr"/> is set. Composition is left-to-right with no parentheses
    /// (single flat OR level, per the YAGNI boundary).
    /// </summary>
    protected string RenderPredicates(IReadOnlyList<SqlPredicate> predicates)
    {
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < predicates.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(predicates[i].IsOr ? " OR " : " AND ");
            }

            sb.Append(RenderPredicate(predicates[i]));
        }

        return sb.ToString();
    }

    /// <summary>Renders a single criterion. Dispatches LIKE and IN to overridable hooks.</summary>
    protected string RenderPredicate(SqlPredicate predicate)
    {
        // A JSON-path criterion compares the dialect's extraction of the path inside the JSON column
        // ([InquiryWhere.JsonPath]); an ordinary criterion compares the bare quoted column.
        var column = predicate.JsonPath is { } jsonPath
            ? RenderJsonPathExtract(QuoteIdentifier(predicate.Column.ColumnName), jsonPath)
            : QuoteIdentifier(predicate.Column.ColumnName);
        switch (predicate.Op)
        {
            // SqlPredicate carries the bare logical parameter name; apply the dialect sigil here
            // (':' on Oracle, '@' elsewhere) so predicate SQL matches the dialect like every other clause.
            case SqlCompareOp.Equal: return column + " = " + ParameterName(predicate.ParameterName!);
            case SqlCompareOp.NotEqual: return column + " <> " + ParameterName(predicate.ParameterName!);
            case SqlCompareOp.GreaterThan: return column + " > " + ParameterName(predicate.ParameterName!);
            case SqlCompareOp.GreaterThanOrEqual: return column + " >= " + ParameterName(predicate.ParameterName!);
            case SqlCompareOp.LessThan: return column + " < " + ParameterName(predicate.ParameterName!);
            case SqlCompareOp.LessThanOrEqual: return column + " <= " + ParameterName(predicate.ParameterName!);
            case SqlCompareOp.Between: return column + " BETWEEN " + ParameterName(predicate.ParameterName!) + " AND " + ParameterName(predicate.ParameterNameHi!);
            case SqlCompareOp.NotBetween: return column + " NOT BETWEEN " + ParameterName(predicate.ParameterName!) + " AND " + ParameterName(predicate.ParameterNameHi!);
            case SqlCompareOp.IsNull: return column + " IS NULL";
            case SqlCompareOp.IsNotNull: return column + " IS NOT NULL";
            case SqlCompareOp.Like: return RenderLike(column, ParameterName(predicate.ParameterName!));
            // NOT LIKE reuses the LIKE hook so any dialect ESCAPE handling stays consistent.
            case SqlCompareOp.NotLike: return "NOT (" + RenderLike(column, ParameterName(predicate.ParameterName!)) + ")";
            case SqlCompareOp.In: return RenderIn(column, ParameterName(predicate.ParameterName!));
            case SqlCompareOp.NotIn: return RenderNotIn(column, ParameterName(predicate.ParameterName!));
            default: return column + " = " + ParameterName(predicate.ParameterName!);
        }
    }

    /// <summary>
    /// Renders a LIKE criterion. The parameter value carries the pattern (callers escape <c>%</c>/<c>_</c>);
    /// override to add a dialect-specific <c>ESCAPE</c> clause.
    /// </summary>
    protected virtual string RenderLike(string quotedColumn, string parameterName)
        => quotedColumn + " LIKE " + parameterName;

    /// <summary>
    /// Renders an IN criterion as a single-placeholder sentinel — the runtime binder expands the one
    /// parameter into <c>(@p0, @p1, …)</c> or <c>(NULL)</c>/<c>1=0</c> for an empty collection. Override
    /// only if a dialect prefers array parameters (e.g. PostgreSQL <c>= ANY</c>) — and override
    /// <see cref="UseArrayInParameters"/> in lockstep so the emitter binds the collection as a single
    /// array parameter instead of rewriting the command text.
    /// </summary>
    protected virtual string RenderIn(string quotedColumn, string parameterName)
        => quotedColumn + " IN (" + parameterName + ")";

    /// <summary>
    /// Renders a NOT IN criterion as a parenthesized single-placeholder sentinel — the runtime
    /// <c>InquiryInExpansion.ExpandNotIn</c> rewrites the sentinel into
    /// <c>(@p0, @p1, …)</c> for a non-empty collection, or <c>(NULL) OR 1=1</c> for an empty one (an empty
    /// NOT IN matches every row — unlike an empty IN). The outer parens keep that <c>OR 1=1</c> tautology
    /// self-contained when the criterion AND/OR-composes with others. Dialect-uniform (always the sentinel
    /// path, never an array parameter), so an empty collection behaves consistently everywhere.
    /// </summary>
    protected virtual string RenderNotIn(string quotedColumn, string parameterName)
        => "(" + quotedColumn + " NOT IN (" + parameterName + "))";

    /// <summary>
    /// Renders the text extraction of a JSON path (<c>$.a.b</c>) from a JSON column, for the
    /// <c>[InquiryWhere(JsonPath = …)]</c> predicate. The result is compared against a bound parameter
    /// as text. The default uses the SQL/JSON-path form (<c>json_extract(col, '$.a.b')</c>) that SQLite
    /// uses and MySQL/SqlServer/Oracle override toward their own function; PostgreSQL overrides toward
    /// its <c>#&gt;&gt;</c> path operator (a different path syntax). <paramref name="jsonPath"/> is a
    /// compile-time constant attribute argument — never runtime input — so it embeds directly.
    /// </summary>
    protected virtual string RenderJsonPathExtract(string quotedColumn, string jsonPath)
        => "json_extract(" + quotedColumn + ", '" + jsonPath + "')";

    /// <summary>
    /// Translates a SQL/JSON dotted path (<c>$.a.b</c>) into a PostgreSQL <c>#&gt;&gt;</c> text-path
    /// array literal (<c>{a,b}</c>). Shared so the PostgreSQL builder can reuse it. Only dotted object
    /// paths are supported in v1 (no array indices).
    /// </summary>
    protected static string JsonPathToPostgresTextPath(string jsonPath)
    {
        var trimmed = jsonPath.StartsWith("$.", System.StringComparison.Ordinal)
            ? jsonPath.Substring(2)
            : jsonPath.StartsWith("$", System.StringComparison.Ordinal) ? jsonPath.Substring(1) : jsonPath;
        return "{" + trimmed.Replace(".", ",") + "}";
    }

    /// <summary>
    /// True when this dialect binds an IN collection as a single native array parameter
    /// (<see cref="RenderIn"/> emits <c>= ANY(@name)</c>-style SQL) rather than expanding the
    /// sentinel into per-element placeholders at run time. Keeps the command text constant across
    /// list lengths, so server-side prepared statements stay reusable, and lifts the per-element
    /// parameter cap from IN lists. Default false (per-element expansion).
    /// </summary>
    public virtual bool UseArrayInParameters => false;

    /// <summary>
    /// True when the provider has a native bulk-copy API (SqlBulkCopy / binary COPY /
    /// MySqlBulkCopy) and registers an <c>IInquiryBulkCopier</c>. <c>[InquiryBulkInsert]</c>
    /// methods then stream rows through it; on dialects without one the generator compiles the
    /// method down to the multi-row batch-insert body instead. Default false.
    /// </summary>
    public virtual bool SupportsBulkCopy => false;
}
