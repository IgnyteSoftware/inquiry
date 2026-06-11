using Inquiry.Connections;
using Npgsql;
using System.Data.Common;

namespace Inquiry.PostgreSql;

/// <summary>
/// Opens PostgreSQL connections for the Inquiry request pipeline.
/// </summary>
internal sealed class PostgreSqlInquiryConnectionFactory : IInquiryConnectionFactory
{
    private readonly string _connectionString;
    private readonly string? _failoverConnectionString;
    private readonly RetryingConnectionOpener? _retryingOpener;

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
        _failoverConnectionString = options.FailoverConnectionString;

        var detector = CreateDetector(options.Compatibility);
        if (detector is not null)
        {
            _retryingOpener = new RetryingConnectionOpener(detector, options.MaxAttempts, options.RetryBaseDelay, maxDelay: options.RetryMaxDelay);
        }
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
            : _retryingOpener.OpenAsync(ct => OpenCoreAsync(_connectionString, ct), cancellationToken);
    }

    private static ITransientErrorDetector? CreateDetector(PostgreSqlCompatibility compatibility) => compatibility switch
    {
        PostgreSqlCompatibility.CockroachDb => new CockroachDbTransientErrorDetector(),
        PostgreSqlCompatibility.AuroraPostgreSql => new AuroraTransientErrorDetector(),
        _ => null,
    };

    private async ValueTask<DbConnection> OpenCoreAsync(string connectionString, CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(connectionString);
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
