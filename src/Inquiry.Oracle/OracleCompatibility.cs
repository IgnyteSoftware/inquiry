namespace Inquiry.Oracle;

/// <summary>
/// Selects the Oracle-compatible engine a connection factory targets. Controls runtime behaviour
/// only (transient-fault retry); it has no effect on generated SQL.
/// </summary>
public enum OracleCompatibility
{
    /// <summary>
    /// On-premises / vanilla Oracle. No open-time retry is applied.
    /// </summary>
    None = 0,

    /// <summary>
    /// Managed cloud Oracle (OCI Autonomous Database, AWS RDS Oracle). Enables open-time retry
    /// over standard Oracle transient connection error codes.
    /// </summary>
    CloudHosted = 1,
}
