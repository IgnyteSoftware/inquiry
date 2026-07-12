using Inquiry.Generators.Abstractions;

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
    public override string DialectName => "MariaDb";

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
