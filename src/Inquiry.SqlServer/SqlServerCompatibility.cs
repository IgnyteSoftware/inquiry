namespace Inquiry.SqlServer;

/// <summary>
/// Selects the SQL Server-compatible engine a connection factory targets. Controls runtime
/// behaviour only (transient-fault retry / auth); it has no effect on generated SQL.
/// </summary>
public enum SqlServerCompatibility
{
    /// <summary>
    /// On-premises / vanilla SQL Server. No open-time retry is applied.
    /// </summary>
    None = 0,

    /// <summary>
    /// Azure SQL Database. Enables open-time retry over the documented Azure transient
    /// <c>SqlException</c> numbers.
    /// </summary>
    AzureSql = 1,
}
