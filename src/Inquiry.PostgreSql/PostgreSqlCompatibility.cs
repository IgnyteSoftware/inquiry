namespace Inquiry.PostgreSql;

/// <summary>
/// Selects the PostgreSQL-compatible engine a connection factory targets. Controls runtime
/// behaviour only (transient-fault retry); it has no effect on generated SQL.
/// </summary>
public enum PostgreSqlCompatibility
{
    /// <summary>
    /// Vanilla PostgreSQL. No open-time retry is applied.
    /// </summary>
    None = 0,

    /// <summary>
    /// CockroachDB. Enables open-time retry over serialization-failure / connection SQLSTATEs.
    /// </summary>
    CockroachDb = 1,

    /// <summary>
    /// Amazon Aurora PostgreSQL. Enables open-time retry tuned for reader/writer failover, used
    /// together with a multi-host connection string and <c>Target Session Attributes</c>.
    /// </summary>
    AuroraPostgreSql = 2,
}
