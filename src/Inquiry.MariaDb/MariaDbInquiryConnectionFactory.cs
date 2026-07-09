using Inquiry.Connections;
using MySqlConnector;
using System.Data.Common;

namespace Inquiry.MariaDb;

/// <summary>
/// Opens MariaDB connections (via MySqlConnector, which is wire-compatible) for the Inquiry
/// request pipeline.
/// </summary>
internal sealed class MariaDbInquiryConnectionFactory : IInquiryConnectionFactory
{
    private readonly string _connectionString;
    private readonly string? _failoverConnectionString;

    /// <summary>
    /// Initializes a new instance of <see cref="MariaDbInquiryConnectionFactory"/> with default options.
    /// </summary>
    public MariaDbInquiryConnectionFactory(string connectionString)
        : this(connectionString, new MariaDbInquiryOptions())
    {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="MariaDbInquiryConnectionFactory"/>.
    /// </summary>
    public MariaDbInquiryConnectionFactory(string connectionString, MariaDbInquiryOptions options)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string cannot be empty.", nameof(connectionString));
        }

        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        // Inquiry's emulated RETURNING for a database-generated GUID key captures the value in a
        // @_inquiry_genkey user variable; MySqlConnector only treats an unmatched @name as a user
        // variable when AllowUserVariables is enabled (otherwise it throws). Generator-emitted store SQL
        // is compile-time-constant with bound parameters, so this is safe for generated stores. Caveat:
        // for ad-hoc SQL (the IInquiry.Query*/Execute* string overloads), a missing or misspelled @param
        // is now silently treated as a NULL user variable rather than throwing "parameter not found" —
        // callers passing raw command text must name their parameters correctly.
        _connectionString = WithUserVariables(connectionString);
        _failoverConnectionString = options.FailoverConnectionString is { } failover
            && !string.Equals(failover, connectionString, StringComparison.Ordinal)
                ? WithUserVariables(failover)
                : null;
    }

    private static string WithUserVariables(string connectionString)
        => new MySqlConnectionStringBuilder(connectionString) { AllowUserVariables = true }.ConnectionString;

    // AllowLoadLocalInfile is required by MySqlBulkCopy ([InquiryBulkInsert]), which streams rows
    // via LOAD DATA LOCAL INFILE — but it also widens the blast radius of any SQL-injection bug
    // (a malicious LOAD DATA LOCAL statement could read files off the app host). So it is NOT set
    // on regular pipeline connections; only the dedicated bulk-insert connection opts in, and the
    // server still rejects local data unless local_infile=1.
    private static string WithLocalInfile(string connectionString)
        => new MySqlConnectionStringBuilder(connectionString) { AllowLoadLocalInfile = true }.ConnectionString;

    /// <inheritdoc />
    public ValueTask<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        return _failoverConnectionString is { } failover
            ? FailoverConnectionOpener.OpenAsync(OpenCoreAsync, _connectionString, failover, retryingOpener: null, cancellationToken)
            : OpenCoreAsync(_connectionString, cancellationToken);
    }

    /// <summary>
    /// Opens the dedicated bulk-insert connection — the regular connection string plus
    /// <c>AllowLoadLocalInfile=true</c>, scoped to this connection only (see
    /// <see cref="WithLocalInfile"/> for why the flag is not global). Consumed by
    /// <see cref="MariaDbBulkCopier"/>.
    /// </summary>
    internal ValueTask<DbConnection> OpenBulkCopyConnectionAsync(CancellationToken cancellationToken = default)
    {
        return _failoverConnectionString is { } failover
            ? FailoverConnectionOpener.OpenAsync(OpenCoreAsync, WithLocalInfile(_connectionString), WithLocalInfile(failover), retryingOpener: null, cancellationToken)
            : OpenCoreAsync(WithLocalInfile(_connectionString), cancellationToken);
    }

    private async ValueTask<DbConnection> OpenCoreAsync(string connectionString, CancellationToken cancellationToken)
    {
        var connection = new MySqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}
