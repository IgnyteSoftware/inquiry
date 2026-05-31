using Inquiry.Connections;
using Npgsql;
using System.Data.Common;

namespace Inquiry.PostgreSql;

/// <summary>
/// Opens PostgreSQL connections for the Inquiry request pipeline.
/// </summary>
public sealed class PostgreSqlInquiryConnectionFactory : IInquiryConnectionFactory
{
    private readonly string _connectionString;

    /// <summary>
    /// Initializes a new instance of <see cref="PostgreSqlInquiryConnectionFactory"/>.
    /// </summary>
    public PostgreSqlInquiryConnectionFactory(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string cannot be empty.", nameof(connectionString));
        }

        _connectionString = connectionString;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Npgsql keeps server-side prepared statements in a per-physical-connection cache that survives
    /// the managed connection being returned to the pool, so per-command <c>Prepare()</c> pays off
    /// across operations.
    /// </remarks>
    public bool SupportsPersistentPreparedStatements => true;

    /// <inheritdoc />
    public async ValueTask<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connection = new NpgsqlConnection(_connectionString);
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
