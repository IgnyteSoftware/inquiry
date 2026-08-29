using Inquiry.Connections;
using MySqlConnector;
using System.Data.Common;

namespace Inquiry.MariaDb;

/// <summary>
/// Opens MariaDB connections (via MySqlConnector, which is wire-compatible) for the Inquiry
/// request pipeline.
/// </summary>
/// <remarks>
/// Connections are opened from a single, app-lifetime <see cref="MySqlDataSource"/> built once in
/// the constructor (MySqlConnector's recommended model since 2.2). The data source owns the
/// connection pool, so building it once — rather than constructing a fresh <see cref="MySqlConnection"/>
/// from the string per operation — is both the idiomatic shape and the foundation the
/// <c>Inquiry.Aspire</c> integration builds on (Aspire registers a <see cref="DbDataSource"/>).
/// The factory is a DI singleton, so the data source lives for the container's lifetime and is
/// disposed with it (see <see cref="DisposeAsync"/>).
/// </remarks>
internal sealed class MariaDbInquiryConnectionFactory : IInquiryConnectionFactory, IAsyncDisposable, IDisposable
{
    private readonly string _connectionString;
    private readonly string? _failoverConnectionString;
    private readonly RetryingConnectionOpener? _retryingOpener;

    private readonly MySqlDataSource _primaryDataSource;
    private readonly MySqlDataSource? _failoverDataSource;
    private readonly bool _ownsDataSources;

    private readonly Func<CancellationToken, ValueTask<DbConnection>> _openPrimary;

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

        _connectionString = connectionString;

        var failoverConnectionString = options.FailoverConnectionString is { } failover
            && !string.Equals(failover, connectionString, StringComparison.Ordinal)
                ? failover
                : null;
        _failoverConnectionString = failoverConnectionString;

        _primaryDataSource = new MySqlDataSourceBuilder(_connectionString).Build();
        _failoverDataSource = failoverConnectionString is { } fcs
            ? new MySqlDataSourceBuilder(fcs).Build()
            : null;
        _ownsDataSources = true;
        _openPrimary = ct => OpenCoreAsync(_connectionString, ct);

        if (options.Compatibility != MariaDbCompatibility.None)
        {
            _retryingOpener = new RetryingConnectionOpener(
                new MariaDbTransientErrorDetector(),
                options.MaxAttempts,
                options.RetryBaseDelay,
                maxDelay: options.RetryMaxDelay);
        }
    }

    /// <summary>
    /// Initializes a new instance of <see cref="MariaDbInquiryConnectionFactory"/> that opens
    /// connections from an externally owned data source.
    /// </summary>
    public MariaDbInquiryConnectionFactory(MySqlDataSource dataSource)
    {
        _primaryDataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _connectionString = dataSource.ConnectionString;
        _openPrimary = ct => OpenCoreAsync(_connectionString, ct);
    }

    // AllowLoadLocalInfile is required by MySqlBulkCopy ([InquiryBulkInsert]), which streams rows
    // via LOAD DATA LOCAL INFILE — but it also widens the blast radius of any SQL-injection bug
    // (a malicious LOAD DATA LOCAL statement could read files off the app host). So it is NOT set
    // on regular pipeline connections; only the dedicated bulk-insert connection opts in, and the
    // server still rejects local data unless local_infile=1. Bulk copy connections open outside the
    // data source pool intentionally — pool isolation matches their distinct security posture.
    private static string WithLocalInfile(string connectionString)
        => new MySqlConnectionStringBuilder(connectionString) { AllowLoadLocalInfile = true }.ConnectionString;

    /// <inheritdoc />
    public ValueTask<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (_failoverConnectionString is { } failover)
        {
            return FailoverConnectionOpener.OpenAsync(OpenCoreAsync, _connectionString, failover, _retryingOpener, cancellationToken);
        }

        return _retryingOpener is null
            ? OpenCoreAsync(_connectionString, cancellationToken)
            : _retryingOpener.OpenAsync(_openPrimary, cancellationToken);
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
            ? FailoverConnectionOpener.OpenAsync(OpenBulkCoreAsync, WithLocalInfile(_connectionString), WithLocalInfile(failover), _retryingOpener, cancellationToken)
            : OpenBulkCoreAsync(WithLocalInfile(_connectionString), cancellationToken);
    }

    private async ValueTask<DbConnection> OpenCoreAsync(string connectionString, CancellationToken cancellationToken)
    {
        var dataSource = _failoverDataSource is not null
            && !string.Equals(connectionString, _connectionString, StringComparison.Ordinal)
                ? _failoverDataSource
                : _primaryDataSource;

        return await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<DbConnection> OpenBulkCoreAsync(string connectionString, CancellationToken cancellationToken)
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

    /// <summary>Disposes the underlying data source(s), draining their connection pools.</summary>
    public async ValueTask DisposeAsync()
    {
        if (!_ownsDataSources)
        {
            return;
        }

        await _primaryDataSource.DisposeAsync().ConfigureAwait(false);
        if (_failoverDataSource is not null)
        {
            await _failoverDataSource.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Disposes the underlying data source(s), draining their connection pools.</summary>
    public void Dispose()
    {
        if (!_ownsDataSources)
        {
            return;
        }

        _primaryDataSource.Dispose();
        _failoverDataSource?.Dispose();
    }
}
