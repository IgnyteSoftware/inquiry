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

        // Inquiry's emulated RETURNING for a database-generated GUID key captures the value in a
        // @_inquiry_genkey user variable; MySqlConnector only treats an unmatched @name as a user
        // variable when AllowUserVariables is enabled (otherwise it throws). All Inquiry SQL is
        // compile-time-constant text with bound parameters, so enabling this is safe.
        _connectionString = new MySqlConnectionStringBuilder(connectionString)
        {
            AllowUserVariables = true,
        }.ConnectionString;
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
