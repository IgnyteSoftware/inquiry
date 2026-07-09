using Inquiry.Generators.Abstractions;

namespace Inquiry.MySql.Analyzer;

/// <summary>
/// MySQL SQL builder. Inherits shared MySQL-family SQL from <see cref="MySqlFamilySqlBuilder"/>
/// and adds the MySQL 8.0+ <c>JSON_TABLE</c> IN optimization (#169): IN collections bind as a
/// single JSON array parameter instead of per-element sentinel expansion.
/// </summary>
internal sealed class MySqlSqlBuilder : MySqlFamilySqlBuilder
{
    public override string DialectName => "MySql";

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
}
