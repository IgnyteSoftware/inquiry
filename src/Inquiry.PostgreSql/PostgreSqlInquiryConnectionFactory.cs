using Inquiry.Connections;
using Npgsql;
using System.Data.Common;

namespace Inquiry.PostgreSql;

/// <summary>
/// Opens PostgreSQL connections for the Inquiry request pipeline.
/// </summary>
/// <remarks>
/// Connections are opened from a single, app-lifetime <see cref="NpgsqlDataSource"/> built once in
/// the constructor (Npgsql's recommended model since 6.0). The data source owns the connection pool,
/// type mapping, and the server-side prepared-statement cache, so building it once — rather than
/// constructing a fresh <see cref="NpgsqlConnection"/> from the string per operation — is both the
/// idiomatic shape and the foundation the <c>Inquiry.Aspire</c> integration builds on
/// (Aspire registers a <see cref="DbDataSource"/>). The factory is a DI singleton, so the data source
/// lives for the container's lifetime and is disposed with it (see <see cref="DisposeAsync"/>).
/// </remarks>
internal sealed class PostgreSqlInquiryConnectionFactory : IInquiryConnectionFactory, IAsyncDisposable, IDisposable
{
    private readonly string _connectionString;
    private readonly string? _failoverConnectionString;
    private readonly RetryingConnectionOpener? _retryingOpener;

    // One data source per distinct connection string. The pool and the prepared-statement cache live
    // here, so these are built once and reused for every open, then disposed with the factory.
    private readonly NpgsqlDataSource _primaryDataSource;
    private readonly NpgsqlDataSource? _failoverDataSource;
    private readonly bool _ownsDataSources;

    // Cached so the retry path doesn't allocate a closure per open (OpenConnectionAsync runs once
    // per pipeline operation).
    private readonly Func<CancellationToken, ValueTask<DbConnection>> _openPrimary;

    /// <summary>
    /// Initializes a new instance of <see cref="PostgreSqlInquiryConnectionFactory"/> with default
    /// options (<see cref="PostgreSqlCompatibility.None"/>).
    /// </summary>
    public PostgreSqlInquiryConnectionFactory(string connectionString)
        : this(connectionString, new PostgreSqlInquiryOptions())
    {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="PostgreSqlInquiryConnectionFactory"/>.
    /// </summary>
    public PostgreSqlInquiryConnectionFactory(string connectionString, PostgreSqlInquiryOptions options)
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

        // A failover string identical to the primary is a no-op: building a second data source for the
        // same target would just waste a pool, and routing to it never adds resilience. Normalize it away
        // so both the open-path gate (_failoverConnectionString) and the routing gate (_failoverDataSource)
        // stay in lockstep — neither sees a failover that the other doesn't.
        var failoverConnectionString = options.FailoverConnectionString is { } configured
            && !string.Equals(configured, connectionString, StringComparison.Ordinal)
                ? configured
                : null;
        _failoverConnectionString = failoverConnectionString;
        _primaryDataSource = new NpgsqlDataSourceBuilder(connectionString).Build();
        _failoverDataSource = failoverConnectionString is { } failover
            ? new NpgsqlDataSourceBuilder(failover).Build()
            : null;
        _ownsDataSources = true;
        _openPrimary = ct => OpenCoreAsync(_connectionString, ct);

        var detector = CreateDetector(options.Compatibility);
        if (detector is not null)
        {
            _retryingOpener = new RetryingConnectionOpener(detector, options.MaxAttempts, options.RetryBaseDelay, maxDelay: options.RetryMaxDelay);
        }
    }

    /// <summary>
    /// Initializes a new instance of <see cref="PostgreSqlInquiryConnectionFactory"/> that opens
    /// connections from an externally owned data source.
    /// </summary>
    public PostgreSqlInquiryConnectionFactory(NpgsqlDataSource dataSource)
    {
        _primaryDataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _connectionString = dataSource.ConnectionString;
        _openPrimary = ct => OpenCoreAsync(_connectionString, ct);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Npgsql keeps server-side prepared statements in a per-physical-connection cache that survives
    /// the managed connection being returned to the pool, so per-command <c>Prepare()</c> pays off
    /// across operations.
    /// </remarks>
    public bool SupportsPersistentPreparedStatements => true;

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

    private static ITransientErrorDetector? CreateDetector(PostgreSqlCompatibility compatibility) => compatibility switch
    {
        PostgreSqlCompatibility.CockroachDb => new CockroachDbTransientErrorDetector(),
        PostgreSqlCompatibility.AuroraPostgreSql => new AuroraTransientErrorDetector(),
        _ => null,
    };

    private async ValueTask<DbConnection> OpenCoreAsync(string connectionString, CancellationToken cancellationToken)
    {
        // The failover opener and retry opener route through this string-keyed entry point; map the
        // string back to the data source built for it. Only the primary and (optional) failover strings
        // ever reach here, so an ordinal compare is sufficient.
        var dataSource = _failoverDataSource is not null
            && !string.Equals(connectionString, _connectionString, StringComparison.Ordinal)
                ? _failoverDataSource
                : _primaryDataSource;

        // NpgsqlDataSource.OpenConnectionAsync pulls from the pool and disposes the connection itself on
        // open failure, so the explicit try/dispose the raw-connection path needed is no longer required.
        return await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
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
