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
        => "SELECT " + context.SelectColumns + " FROM " + context.Table + " WHERE " + RenderPredicates(predicates);

    public abstract string BuildInsertSql(SqlBuildContext context);

    public abstract string BuildInsertReturningSql(SqlBuildContext context);

    public abstract string BuildUpdateSql(SqlBuildContext context);

    public abstract string BuildUpdateReturningSql(SqlBuildContext context);

    public abstract string BuildDeleteByKeySql(SqlBuildContext context);

    public abstract string BuildUpsertSql(SqlBuildContext context);

    public abstract string BuildUpsertReturningSql(SqlBuildContext context);

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
