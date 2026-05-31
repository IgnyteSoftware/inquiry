using Microsoft.Data.SqlClient;

namespace Inquiry.SqlServer;

/// <summary>
/// Provider-specific runtime options for the SQL Server connection factory. Configured via the
/// <c>AddInquirySqlServer(connectionString, Action&lt;SqlServerInquiryOptions&gt;)</c> DI overload.
/// </summary>
/// <remarks>
/// Separate from the core <c>Inquiry.InquiryOptions</c> (prepared-statement mode); these options
/// only govern connection open behaviour (cloud transient retry / access-token auth).
/// </remarks>
public sealed class SqlServerInquiryOptions
{
    /// <summary>
    /// Gets or sets the target SQL Server-compatible engine. Defaults to
    /// <see cref="SqlServerCompatibility.None"/> (no open-time retry).
    /// </summary>
    public SqlServerCompatibility Compatibility { get; set; } = SqlServerCompatibility.None;

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
    /// Gets or sets an optional callback that supplies an Azure AD / Entra access token assigned to
    /// <see cref="SqlConnection.AccessToken"/> before the connection is opened. Leave
    /// <see langword="null"/> when authentication is handled entirely by the connection string
    /// (e.g. <c>Authentication=Active Directory Default</c>).
    /// </summary>
    public Func<CancellationToken, ValueTask<string>>? AccessTokenProvider { get; set; }
}
