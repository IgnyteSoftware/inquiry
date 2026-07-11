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

}
