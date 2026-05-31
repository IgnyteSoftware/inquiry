using Inquiry.Connections;
using MySqlConnector;
using System.Data.Common;

namespace Inquiry.MySql;

/// <summary>
/// Opens MySQL/MariaDB connections for the Inquiry request pipeline.
/// </summary>
public sealed class MySqlInquiryConnectionFactory : IInquiryConnectionFactory
{
    private readonly string _connectionString;

    /// <summary>
    /// Initializes a new instance of <see cref="MySqlInquiryConnectionFactory"/>.
    /// </summary>
    public MySqlInquiryConnectionFactory(string connectionString)
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
        var connection = new MySqlConnection(_connectionString);
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
