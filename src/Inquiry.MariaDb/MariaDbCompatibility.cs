namespace Inquiry.MariaDb;

/// <summary>
/// Selects the MariaDB-compatible engine a connection factory targets. Controls runtime behaviour
/// only (transient-fault retry); it has no effect on generated SQL.
/// </summary>
public enum MariaDbCompatibility
{
    /// <summary>
    /// On-premises / vanilla MariaDB. No open-time retry is applied.
    /// </summary>
    None = 0,

    /// <summary>
    /// Managed cloud MariaDB (AWS RDS, Azure Database for MariaDB, SkySQL). Enables open-time
    /// retry over standard MariaDB transient connection error codes.
    /// </summary>
    CloudHosted = 1,
}
