namespace Inquiry.MySql;

/// <summary>
/// Selects the MySQL-compatible engine a connection factory targets. Controls runtime behaviour
/// only (transient-fault retry); it has no effect on generated SQL.
/// </summary>
public enum MySqlCompatibility
{
    /// <summary>
    /// On-premises / vanilla MySQL. No open-time retry is applied.
    /// </summary>
    None = 0,

    /// <summary>
    /// Managed cloud MySQL (AWS RDS, Azure Database for MySQL, Google Cloud SQL). Enables
    /// open-time retry over standard MySQL transient connection error codes.
    /// </summary>
    CloudHosted = 1,
}
