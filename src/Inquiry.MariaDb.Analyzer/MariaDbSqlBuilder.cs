using Inquiry.Generators.Abstractions;

namespace Inquiry.MariaDb.Analyzer;

/// <summary>
/// MariaDB SQL builder. Inherits shared MySQL-family SQL (backtick quoting, <c>ON DUPLICATE KEY UPDATE</c>
/// upsert) from <see cref="MySqlFamilySqlBuilder"/> and overrides the returning paths with MariaDB 10.5+
/// native <c>INSERT…RETURNING</c> (#58). <c>UPDATE…RETURNING</c> is not supported by MariaDB, so
/// <see cref="MySqlFamilySqlBuilder.BuildUpdateReturningSql"/> keeps its emulated two-statement batch.
/// </summary>
internal sealed class MariaDbSqlBuilder : MySqlFamilySqlBuilder
{
    public override string DialectName => "MariaDb";

    public override string BuildInsertReturningSql(SqlBuildContext context)
    {
        if (DatabaseMaySupplyKey(context) && context.KeyColumns[0].TypeClass == DbTypeClass.Guid)
        {
            var keyColumn = context.QuotedKeyColumns[0];
            var keyValue = "COALESCE(" + context.KeyParameters[0] + ", UUID())";
            var cols = string.IsNullOrEmpty(context.InsertColumns) ? keyColumn : keyColumn + ", " + context.InsertColumns;
            var vals = string.IsNullOrEmpty(context.InsertParameters) ? keyValue : keyValue + ", " + context.InsertParameters;

            return "INSERT INTO " + context.Table + " (" + cols + ") VALUES (" + vals + ") RETURNING " + context.SelectColumns;
        }

        return BuildInsertSql(context) + " RETURNING " + context.SelectColumns;
    }

    public override string BuildUpsertReturningSql(SqlBuildContext context)
        => BuildUpsertSql(context) + " RETURNING " + context.SelectColumns;
}
