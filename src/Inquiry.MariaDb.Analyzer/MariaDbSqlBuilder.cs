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

    // ---- Native RETURNING (#58) -------------------------------------------------------------

    public override string BuildInsertReturningSql(SqlBuildContext context)
        => BuildInsertSql(context) + " RETURNING " + context.SelectColumns;

    public override string BuildUpsertReturningSql(SqlBuildContext context)
        => BuildUpsertSql(context) + " RETURNING " + context.SelectColumns;

    public override string BuildDeleteByKeyReturningSql(SqlBuildContext context)
        => BuildDeleteByKeySql(context) + " RETURNING " + context.SelectColumns;
}
