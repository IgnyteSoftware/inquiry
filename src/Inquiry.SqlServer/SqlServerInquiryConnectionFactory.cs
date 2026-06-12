using Inquiry.Connections;
using Microsoft.Data.SqlClient;
using System.Data.Common;

namespace Inquiry.SqlServer;

/// <summary>
/// Opens SQL Server connections for generated Inquiry stores.
/// </summary>
internal sealed class SqlServerInquiryConnectionFactory : IInquiryConnectionFactory
{
    private readonly string _connectionString;
    private readonly SqlServerInquiryOptions _options;
    private readonly RetryingConnectionOpener? _retryingOpener;

    // Cached so the retry path doesn't allocate a closure per open (OpenConnectionAsync runs once
    // per pipeline operation).
    private readonly Func<CancellationToken, ValueTask<DbConnection>> _openPrimary;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqlServerInquiryConnectionFactory"/> class with
    /// default options (<see cref="SqlServerCompatibility.None"/>).
    /// </summary>
    public SqlServerInquiryConnectionFactory(string connectionString)
        : this(connectionString, new SqlServerInquiryOptions())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SqlServerInquiryConnectionFactory"/> class.
    /// </summary>
    public SqlServerInquiryConnectionFactory(string connectionString, SqlServerInquiryOptions options)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string cannot be empty.", nameof(connectionString));
        }

        _connectionString = connectionString;
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _openPrimary = ct => OpenCoreAsync(_connectionString, ct);

        if (_options.Compatibility != SqlServerCompatibility.None)
        {
            _retryingOpener = new RetryingConnectionOpener(
                new SqlServerTransientErrorDetector(),
                _options.MaxAttempts,
                _options.RetryBaseDelay,
                maxDelay: _options.RetryMaxDelay);
        }
    }

    /// <inheritdoc />
    public ValueTask<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (_options.FailoverConnectionString is { } failover)
        {
            return FailoverConnectionOpener.OpenAsync(OpenCoreAsync, _connectionString, failover, _retryingOpener, cancellationToken);
        }

        return _retryingOpener is null
            ? OpenCoreAsync(_connectionString, cancellationToken)
            : _retryingOpener.OpenAsync(_openPrimary, cancellationToken);
    }

    private async ValueTask<DbConnection> OpenCoreAsync(string connectionString, CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(connectionString);

        try
        {
            if (_options.AccessTokenProvider is not null)
            {
                connection.AccessToken = await _options.AccessTokenProvider(cancellationToken).ConfigureAwait(false);
            }

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
