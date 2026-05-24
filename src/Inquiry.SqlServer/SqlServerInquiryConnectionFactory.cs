using Inquiry.Connections;
using Microsoft.Data.SqlClient;
using System.Data.Common;

namespace Inquiry.SqlServer;

/// <summary>
/// Opens SQL Server connections for generated Inquiry stores.
/// </summary>
public sealed class SqlServerInquiryConnectionFactory : IInquiryConnectionFactory
{
    private readonly string _connectionString;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqlServerInquiryConnectionFactory"/> class.
    /// </summary>
    public SqlServerInquiryConnectionFactory(string connectionString)
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
        var connection = new SqlConnection(_connectionString);

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
