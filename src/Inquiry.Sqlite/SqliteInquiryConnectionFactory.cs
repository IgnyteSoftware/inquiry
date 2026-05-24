using Inquiry.Connections;
using Microsoft.Data.Sqlite;
using System.Data.Common;

namespace Inquiry.Sqlite;

/// <summary>
/// Opens SQLite connections for generated Inquiry stores.
/// </summary>
public sealed class SqliteInquiryConnectionFactory : IInquiryConnectionFactory
{
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

    /// <inheritdoc />
    public async ValueTask<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
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
