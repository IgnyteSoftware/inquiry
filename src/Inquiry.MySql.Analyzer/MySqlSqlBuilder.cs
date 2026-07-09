using Inquiry.Generators.Abstractions;

namespace Inquiry.MySql.Analyzer;

/// <summary>
/// MySQL SQL builder. All SQL shapes are inherited from <see cref="MySqlFamilySqlBuilder"/> (backtick
/// quoting, <c>ON DUPLICATE KEY UPDATE</c> upsert, emulated returning batches); MySQL-specific
/// divergence (e.g. the MySQL 8.0+ <c>JSON_TABLE</c> IN optimization, #169) lands here.
/// </summary>
internal sealed class MySqlSqlBuilder : MySqlFamilySqlBuilder
{
    public override string DialectName => "MySql";
}
