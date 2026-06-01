using System.Collections.Generic;

namespace Inquiry.Generators.Abstractions;

/// <summary>
/// Compile-time SQL builder consumed by the Inquiry source generator. One concrete subclass exists
/// per supported dialect, lives in that provider's analyzer assembly, and is registered with
/// <see cref="SqlBuilderRegistry"/> at analyzer load time. The Inquiry runtime ships no SQL — every
/// statement is produced here and emitted as a <c>const string</c> field at compile time.
/// </summary>
/// <remarks>
/// FOUNDATION CONVENTION (Phase 0 / F3): when a feature workstream adds a new capability, prefer a
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
    /// Builds a SELECT whose WHERE clause is the AND/OR composition of <paramref name="predicates"/>.
    /// Dialect-uniform: the base implementation renders every operator portably (comparison, BETWEEN,
    /// IS [NOT] NULL, plus the <see cref="RenderLike"/>/<see cref="RenderIn"/> hooks). Providers only
    /// override a hook when their LIKE/IN syntax differs. The composed predicate body is routed through
    /// <see cref="AppendWhere"/> so it stays consistent with key/field WHERE shaping.
    /// </summary>
    public virtual string BuildSelectByPredicateSql(SqlBuildContext context, IReadOnlyList<SqlPredicate> predicates)
        => "SELECT " + context.SelectColumns + " FROM " + context.Table
            + " WHERE " + AppendWhere(RenderPredicates(predicates), context.SoftDeleteActivePredicate);

    public abstract string BuildInsertSql(SqlBuildContext context);

    public abstract string BuildInsertReturningSql(SqlBuildContext context);

    public abstract string BuildUpdateSql(SqlBuildContext context);

    public abstract string BuildUpdateReturningSql(SqlBuildContext context);

    public abstract string BuildDeleteByKeySql(SqlBuildContext context);

    // ---- Soft delete (W8) -------------------------------------------------------------------

    /// <summary>
    /// SQL literal for an active (not-deleted) boolean soft-delete flag. Default <c>0</c> (SQLite/
    /// SqlServer/MySQL); PostgreSQL overrides with <c>FALSE</c>.
    /// </summary>
    public virtual string SoftDeleteFalseLiteral => "0";

    /// <summary>
    /// SQL literal for a deleted boolean soft-delete flag. Default <c>1</c> (SQLite/SqlServer/MySQL);
    /// PostgreSQL overrides with <c>TRUE</c>.
    /// </summary>
    public virtual string SoftDeleteTrueLiteral => "1";

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

    public abstract string BuildUpsertSql(SqlBuildContext context);

    public abstract string BuildUpsertReturningSql(SqlBuildContext context);

    /// <summary>
    /// W5: builds a <c>SELECT COUNT(*)</c> over the entity's table. Dialect-uniform (ANSI), so this is
    /// concrete and inherited by every provider; it composes the soft-delete active filter via
    /// <see cref="WhereSuffix"/> so a count excludes soft-deleted rows when applicable.
    /// </summary>
    public virtual string BuildCountSql(SqlBuildContext context)
        => "SELECT COUNT(*) FROM " + context.Table + WhereSuffix(context.SoftDeleteActivePredicate);

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
    /// Builds the keyset comparison predicate body (no leading <c>WHERE</c>) for the cursor, wrapped in a
    /// <c>(@cursor IS NULL OR …)</c> guard so a null cursor selects from the start of the (first) page.
    /// The portable default uses a row-value comparison <c>(a, b) &gt; (@c0, @c1)</c>; SQL Server, which
    /// lacks row-value <c>&gt;</c>, overrides with the lexicographic OR-form. Single-column keysets use a
    /// plain scalar comparison in both.
    /// </summary>
    public virtual string BuildKeysetPredicate(SqlSelectOptions options)
    {
        var op = options.KeysetDescending ? " < " : " > ";
        var firstCursor = options.KeysetCursorParameters[0];

        if (options.KeysetColumns.Count == 1)
        {
            return "(" + firstCursor + " IS NULL OR " + options.KeysetColumns[0] + op + firstCursor + ")";
        }

        var columns = "(" + string.Join(", ", options.KeysetColumns) + ")";
        var cursors = "(" + string.Join(", ", options.KeysetCursorParameters) + ")";
        return "(" + firstCursor + " IS NULL OR " + columns + op + cursors + ")";
    }

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
            : whereClause + " AND " + extraPredicate;
    }

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
    /// (single flat OR level, per W1's YAGNI boundary).
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
        var column = QuoteIdentifier(predicate.Column.ColumnName);
        switch (predicate.Op)
        {
            case SqlCompareOp.Equal: return column + " = " + predicate.ParameterName;
            case SqlCompareOp.NotEqual: return column + " <> " + predicate.ParameterName;
            case SqlCompareOp.GreaterThan: return column + " > " + predicate.ParameterName;
            case SqlCompareOp.GreaterThanOrEqual: return column + " >= " + predicate.ParameterName;
            case SqlCompareOp.LessThan: return column + " < " + predicate.ParameterName;
            case SqlCompareOp.LessThanOrEqual: return column + " <= " + predicate.ParameterName;
            case SqlCompareOp.Between: return column + " BETWEEN " + predicate.ParameterName + " AND " + predicate.ParameterNameHi;
            case SqlCompareOp.IsNull: return column + " IS NULL";
            case SqlCompareOp.IsNotNull: return column + " IS NOT NULL";
            case SqlCompareOp.Like: return RenderLike(column, predicate.ParameterName!);
            case SqlCompareOp.In: return RenderIn(column, predicate.ParameterName!);
            default: return column + " = " + predicate.ParameterName;
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
    /// only if a dialect prefers array parameters (e.g. PostgreSQL <c>= ANY</c>).
    /// </summary>
    protected virtual string RenderIn(string quotedColumn, string parameterName)
        => quotedColumn + " IN (" + parameterName + ")";
}
