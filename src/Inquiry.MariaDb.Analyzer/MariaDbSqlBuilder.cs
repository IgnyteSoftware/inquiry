using Inquiry.Generators.Abstractions;

namespace Inquiry.MariaDb.Analyzer;

/// <summary>
/// MariaDB SQL builder. All SQL shapes are inherited from <see cref="MySqlFamilySqlBuilder"/> (backtick
/// quoting, <c>ON DUPLICATE KEY UPDATE</c> upsert, emulated returning batches) — deliberately identical
/// to the MySQL builder for the dialect split (#168). MariaDB-specific divergence (native
/// <c>INSERT…RETURNING</c> #58, the MariaDB 10.6+ <c>JSON_TABLE</c> IN optimization #170) lands here.
/// </summary>
internal sealed class MariaDbSqlBuilder : MySqlFamilySqlBuilder
{
    public override string DialectName => "MariaDb";
}
