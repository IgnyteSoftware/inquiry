namespace Inquiry.MySql;

/// <summary>
/// Provider-specific runtime options for the MySQL/MariaDB connection factory. Configured via the
/// <c>AddInquiryMySql(connectionString, Action&lt;MySqlInquiryOptions&gt;)</c> DI overload.
/// </summary>
/// <remarks>
/// Separate from the core <c>Inquiry.InquiryOptions</c> (prepared-statement mode); these options
/// only govern connection open behaviour (failover).
/// </remarks>
public sealed class MySqlInquiryOptions
{
    /// <summary>
    /// Gets or sets an optional backup-server connection string. When the primary connection
    /// string fails to open, the factory opens against this connection string instead. Every open
    /// tries the primary first, so traffic returns to the primary automatically once it recovers.
    /// Defaults to <see langword="null"/> (no failover). MySqlConnector also supports listing
    /// multiple hosts directly in one connection string (<c>Server=primary,backup</c>) when
    /// driver-level failover is preferred.
    /// </summary>
    public string? FailoverConnectionString { get; set; }
}
