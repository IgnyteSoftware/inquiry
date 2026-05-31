using Inquiry.Generators.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Inquiry.Oracle.Analyzer;

/// <summary>
/// Oracle 12c+ SQL builder. Three things set Oracle apart from the PostgreSQL/SQL Server shape it
/// otherwise mirrors:
/// <list type="bullet">
/// <item><description>Bind parameters use the <c>:name</c> prefix instead of <c>@name</c>. The SQL
/// text is corrected here via <see cref="ParameterName"/>; the runtime binder still emits
/// <c>@name</c> (shared, unmodified) and <c>OracleCommand.BindByName = true</c> — set in
/// <c>OracleInquiryConnectionFactory.InitializeCommand</c> — matches the two by name.</description></item>
/// <item><description>Identifiers are left <b>unquoted</b>. Oracle folds unquoted identifiers to
/// uppercase, so the Northwind DDL is created unquoted to match; blanket double-quoting would force
/// exact-case names that the unquoted DDL would not resolve.</description></item>
/// <item><description><c>RETURNING … INTO</c> binds OUT parameters rather than producing a result
/// set, so it is incompatible with the reader-based returning pipeline. v1 does not support
/// <c>ReturnEntity = true</c> insert/update/upsert (see the returning builders below).</description></item>
/// </list>
/// </summary>
internal sealed class OracleSqlBuilder : SqlBuilder
{
    public override string DialectName => "Oracle";

    /// <summary>Oracle bind variables use the <c>:name</c> prefix.</summary>
    public override string ParameterName(string logicalName) => ":" + logicalName;

    /// <summary>
    /// Unquoted, uppercase-folding identifier policy. Oracle uppercases unquoted identifiers, and the
    /// provider's DDL is written unquoted to match, so emitting names verbatim (no quoting) keeps the
    /// generated SQL aligned with the schema.
    /// </summary>
    public override string QuoteIdentifier(string identifier) => identifier;

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
            // Oracle has no DEFAULT VALUES clause; VALUES (DEFAULT) inserts an all-defaults row.
            return "INSERT INTO " + context.Table + " VALUES (DEFAULT)";
        }

        return "INSERT INTO " + context.Table
            + " (" + context.InsertColumns + ") VALUES (" + context.InsertParameters + ")";
    }

    public override string BuildUpdateSql(SqlBuildContext context)
        => "UPDATE " + context.Table + " SET " + context.SetClauses + " WHERE " + context.KeyWhereClause;

    public override string BuildDeleteByKeySql(SqlBuildContext context)
        => "DELETE FROM " + context.Table + " WHERE " + context.KeyWhereClause;

    public override string BuildUpsertSql(SqlBuildContext context)
    {
        var insertColumns = context.InsertColumns;
        var insertParameters = context.InsertParameters;

        return
            "MERGE INTO " + context.Table + " target USING (" + BuildSourceSelect(context) + ") source " +
            "ON (" + BuildSourceJoin(context) + ") " +
            "WHEN MATCHED THEN UPDATE SET " + context.SetClauses + " " +
            "WHEN NOT MATCHED THEN INSERT (" + insertColumns + ") VALUES (" + insertParameters + ")";
    }

    // --- Returning DML: unsupported in v1 (Oracle RETURNING … INTO is not result-set-based). ---
    // These builders are only reached when a store declares an [InquiryInsert/Update/Upsert]
    // (ReturnEntity = true) method against the Oracle dialect. Rather than emit SQL the reader
    // pipeline cannot consume (silent runtime failure), fail the build with a clear message so the
    // user switches to the non-returning variant.
    private const string ReturningUnsupportedMessage =
        "Inquiry Oracle provider (v1) does not support ReturnEntity = true. Oracle RETURNING … INTO " +
        "binds OUT parameters instead of producing a result set the reader pipeline can consume. Use " +
        "the non-returning Insert/Update/Upsert and re-select by key, or target a provider that " +
        "supports result-set RETURNING (PostgreSql/SqlServer).";

    public override string BuildInsertReturningSql(SqlBuildContext context)
        => throw new NotSupportedException(ReturningUnsupportedMessage);

    public override string BuildUpdateReturningSql(SqlBuildContext context)
        => throw new NotSupportedException(ReturningUnsupportedMessage);

    public override string BuildUpsertReturningSql(SqlBuildContext context)
        => throw new NotSupportedException(ReturningUnsupportedMessage);

    /// <summary>
    /// Oracle 12c+ offset pagination uses the ANSI <c>OFFSET … ROWS FETCH NEXT … ROWS ONLY</c> form
    /// (same as SQL Server), which requires a preceding ORDER BY (enforced in the generator).
    /// </summary>
    public override string BuildPaginationClause(SqlSelectOptions options)
        => "OFFSET " + options.OffsetParameter + " ROWS FETCH NEXT " + options.LimitParameter + " ROWS ONLY";

    /// <summary>
    /// Oracle does not support a row-value <c>(a, b) &gt; (@c0, @c1)</c> comparison, so a multi-column
    /// keyset renders the lexicographic OR-form <c>(a &gt; @c0) OR (a = @c0 AND b &gt; @c1)</c>.
    /// Single-column keysets use the portable scalar form.
    /// </summary>
    public override string BuildKeysetPredicate(SqlSelectOptions options)
    {
        if (options.KeysetColumns.Count == 1)
        {
            return base.BuildKeysetPredicate(options);
        }

        var op = options.KeysetDescending ? " < " : " > ";
        var firstCursor = options.KeysetCursorParameters[0];
        var sb = new System.Text.StringBuilder();
        sb.Append('(').Append(firstCursor).Append(" IS NULL OR (");
        for (var i = 0; i < options.KeysetColumns.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(" OR ");
            }

            sb.Append('(');
            for (var j = 0; j < i; j++)
            {
                sb.Append(options.KeysetColumns[j]).Append(" = ").Append(options.KeysetCursorParameters[j]).Append(" AND ");
            }

            sb.Append(options.KeysetColumns[i]).Append(op).Append(options.KeysetCursorParameters[i]).Append(')');
        }

        sb.Append("))");
        return sb.ToString();
    }

    private static string BuildSourceSelect(SqlBuildContext context)
        => "SELECT " + string.Join(", ", context.KeyParameters.Select((p, i) => p + " AS k" + i)) + " FROM dual";

    private static string BuildSourceJoin(SqlBuildContext context)
        => string.Join(" AND ", context.QuotedKeyColumns.Select((q, i) => "target." + q + " = source.k" + i));
}
