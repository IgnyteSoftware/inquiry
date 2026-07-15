using Inquiry.Generators.Abstractions;
using System.Collections.Generic;
using System.Linq;

namespace Inquiry.MariaDb.Analyzer;

/// <summary>
/// MariaDB SQL builder. Inherits shared MySQL-family SQL (backtick quoting, <c>ON DUPLICATE KEY UPDATE</c>
/// upsert) from <see cref="MySqlFamilySqlBuilder"/> and overrides: (1) returning paths with MariaDB 10.5+
/// native <c>INSERT…RETURNING</c> (#58) — <c>UPDATE…RETURNING</c> is not supported by MariaDB, so
/// <see cref="MySqlFamilySqlBuilder.BuildUpdateReturningSql"/> keeps its emulated two-statement batch;
/// (2) IN collection binding with MariaDB 10.6+ <c>JSON_TABLE</c> (#170).
/// </summary>
internal sealed class MariaDbSqlBuilder : MySqlFamilySqlBuilder
{
    protected override SqlExpressionCommentPolicy ComputedExpressionCommentPolicy => SqlExpressionCommentPolicy.MariaDb;
    public override IReadOnlyList<string> ValidateComputedExpression(string expression)
    {
        var analysis = SqlExpressionLexer.Analyze(expression, ComputedExpressionCommentPolicy, false);
        if (!analysis.HasConcatenationOperator) return analysis.Failures;
        return analysis.Failures.Concat(new[] { "contains ambiguous '||'; use CONCAT(...), OR, or a mariadb override" }).ToArray();
    }
    public override string DialectName => "MariaDb";
    public override string ProviderId => "mariadb";

    public override CyclicForeignKeyStrategy CyclicForeignKeyStrategy => CyclicForeignKeyStrategy.AlterTable;
    public override bool SupportsCheckConstraints => true;
    public override ConstraintNameScope IndexNameScope => ConstraintNameScope.Table;
    public override IdentifierComparison IndexNameComparison => IdentifierComparison.OrdinalIgnoreCase;
    public override IdentifierComparison CheckConstraintNameComparison => IdentifierComparison.OrdinalIgnoreCase;
    public override IdentifierComparison ForeignKeyConstraintNameComparison => IdentifierComparison.OrdinalIgnoreCase;
    public override bool SupportsReferentialAction(ReferentialActionKind action, ReferentialActionEvent @event) => action is ReferentialActionKind.NoAction or ReferentialActionKind.Restrict or ReferentialActionKind.Cascade or ReferentialActionKind.SetNull;

    // ---- Native RETURNING (#58) -------------------------------------------------------------

    public override string BuildInsertReturningSql(SqlBuildContext context)
        => BuildInsertSql(context) + " RETURNING " + context.SelectColumns;

    public override string BuildUpsertReturningSql(SqlBuildContext context)
        => BuildUpsertSql(context) + " RETURNING " + context.SelectColumns;

    public override string BuildDeleteByKeyReturningSql(SqlBuildContext context)
        => BuildDeleteByKeySql(context) + " RETURNING " + context.SelectColumns;
}
