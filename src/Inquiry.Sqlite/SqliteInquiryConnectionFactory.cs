using Inquiry.Connections;
using Microsoft.Data.Sqlite;
using System.Data.Common;

namespace Inquiry.Sqlite;

/// <summary>
/// Opens SQLite connections for generated Inquiry stores.
/// </summary>
internal sealed class SqliteInquiryConnectionFactory : IInquiryConnectionFactory
{
    private readonly DbDataSource? _dataSource;
    private readonly string _connectionString;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqliteInquiryConnectionFactory"/> class.
    /// </summary>
    public SqliteInquiryConnectionFactory(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string cannot be empty.", nameof(connectionString));
        }

        _connectionString = connectionString;
    }

    /// <summary>
    /// Initializes a new instance of <see cref="SqliteInquiryConnectionFactory"/> that opens
    /// connections from an externally owned data source.
    /// </summary>
    public SqliteInquiryConnectionFactory(DbDataSource dataSource)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _connectionString = dataSource.ConnectionString;
    }

    /// <inheritdoc />
    public async ValueTask<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (_dataSource is not null)
        {
            return await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        }

        var connection = new SqliteConnection(_connectionString);

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
