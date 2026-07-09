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

    // ---- JSON_TABLE IN optimization (#170) --------------------------------------------------

    public override bool UseArrayInParameters => true;

    public override string ArrayParameterBinderFqn => "global::Inquiry.Parameters.InquiryJsonArrayParameter";

    protected override string RenderIn(string quotedColumn, string parameterName, DbTypeClass elementType)
    {
        var colType = elementType switch
        {
            DbTypeClass.Boolean or DbTypeClass.Byte or DbTypeClass.Int16
                or DbTypeClass.Int32 or DbTypeClass.Int64 => "SIGNED",
            DbTypeClass.Single or DbTypeClass.Double => "DOUBLE",
            DbTypeClass.Decimal => "DECIMAL(65,30)",
            DbTypeClass.Guid => "CHAR(36)",
            _ => "CHAR(255)",
        };

        return quotedColumn + " IN (SELECT jt.val FROM JSON_TABLE(" + parameterName
            + ", '$[*]' COLUMNS(val " + colType + " PATH '$')) jt)";
    }

    // ---- Native RETURNING (#58) -------------------------------------------------------------

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
