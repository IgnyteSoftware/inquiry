namespace Inquiry.PostgreSql;

/// <summary>
/// Provider-specific runtime options for the PostgreSQL connection factory. Configured via the
/// <c>AddInquiryPostgreSql(connectionString, Action&lt;PostgreSqlInquiryOptions&gt;)</c> DI overload.
/// </summary>
/// <remarks>
/// Separate from the core <c>Inquiry.InquiryOptions</c> (prepared-statement mode); these options
/// only govern connection open behaviour (cloud transient retry / failover).
/// </remarks>
public sealed class PostgreSqlInquiryOptions
{
    /// <summary>
    /// Gets or sets the target PostgreSQL-compatible engine. Defaults to
    /// <see cref="PostgreSqlCompatibility.None"/> (no open-time retry).
    /// </summary>
    public PostgreSqlCompatibility Compatibility { get; set; } = PostgreSqlCompatibility.None;

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
}
