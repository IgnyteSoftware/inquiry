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
}
