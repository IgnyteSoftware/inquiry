namespace Inquiry.Oracle;

/// <summary>
/// Provider-specific runtime options for the Oracle connection factory. Configured via the
/// <c>AddInquiryOracle(connectionString, Action&lt;OracleInquiryOptions&gt;)</c> DI overload.
/// </summary>
/// <remarks>
/// Separate from the core <c>Inquiry.InquiryOptions</c> (prepared-statement mode); these options
/// only govern connection open behaviour (failover).
/// </remarks>
public sealed class OracleInquiryOptions
{
    /// <summary>
    /// Gets or sets an optional backup-server connection string. When the primary connection
    /// string fails to open, the factory opens against this connection string instead. Every open
    /// tries the primary first, so traffic returns to the primary automatically once it recovers.
    /// Defaults to <see langword="null"/> (no failover). Oracle TNS descriptors with
    /// <c>ADDRESS_LIST</c>/<c>FAILOVER=on</c> remain available when driver-level failover is
    /// preferred.
    /// </summary>
    public string? FailoverConnectionString { get; set; }
}
