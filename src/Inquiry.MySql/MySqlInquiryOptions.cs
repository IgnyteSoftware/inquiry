namespace Inquiry.MySql;

/// <summary>
/// Provider-specific runtime options for the MySQL connection factory. Configured via the
/// <c>AddInquiryMySql(connectionString, Action&lt;MySqlInquiryOptions&gt;)</c> DI overload.
/// </summary>
/// <remarks>
/// Separate from the core <c>Inquiry.InquiryOptions</c> (prepared-statement mode); these options
/// only govern connection open behaviour (cloud transient retry / failover).
/// </remarks>
public sealed class MySqlInquiryOptions
{
    /// <summary>
    /// Gets or sets the target MySQL-compatible engine. Defaults to
    /// <see cref="MySqlCompatibility.None"/> (no open-time retry).
    /// </summary>
    public MySqlCompatibility Compatibility { get; set; } = MySqlCompatibility.None;

    /// <summary>
    /// Gets or sets the total number of connection-open attempts (initial try plus retries) used
    /// when <see cref="Compatibility"/> enables retry. Defaults to <c>5</c>.
    /// </summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>
    /// Gets or sets the base exponential-backoff delay between open attempts. Defaults to
    /// <c>200ms</c>.
    /// </summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// Gets or sets the maximum exponential-backoff delay between connection-open attempts.
    /// Defaults to <c>30s</c>.
    /// </summary>
    public TimeSpan RetryMaxDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets an optional backup-server connection string. When the primary connection
    /// string fails to open (after any configured retry), the factory opens against this
    /// connection string instead. Every open tries the primary first, so traffic returns to the
    /// primary automatically once it recovers. Defaults to <see langword="null"/> (no failover).
    /// MySqlConnector also supports listing multiple hosts directly in one connection string
    /// (<c>Server=primary,backup</c>) when driver-level failover is preferred.
    /// </summary>
    public string? FailoverConnectionString { get; set; }
}
