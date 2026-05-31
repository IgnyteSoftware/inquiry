using Inquiry.Connections;
using Oracle.ManagedDataAccess.Client;
using System.Data.Common;

namespace Inquiry.Oracle;

/// <summary>
/// Opens Oracle connections for the Inquiry request pipeline.
/// </summary>
public sealed class OracleInquiryConnectionFactory : IInquiryConnectionFactory
{
    private readonly string _connectionString;

    /// <summary>
    /// Initializes a new instance of <see cref="OracleInquiryConnectionFactory"/>.
    /// </summary>
    public OracleInquiryConnectionFactory(string connectionString)
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
        var connection = new OracleConnection(_connectionString);
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

    /// <summary>
    /// Enables <see cref="OracleCommand.BindByName"/> so parameters bind by name. ODP.NET binds
    /// positionally by default; Inquiry emits named parameters (<c>:name</c> in the SQL text), so
    /// name-binding is required. It also makes ODP.NET tolerant of the prefix mismatch between the
    /// runtime binder's <c>@name</c> parameter names (the shared, dialect-agnostic emitter) and the
    /// <c>:name</c> references baked into the Oracle SQL — see the OPEN QUESTION in the E2 report.
    /// </summary>
    public void InitializeCommand(DbCommand command)
    {
        if (command is OracleCommand oracleCommand)
        {
            oracleCommand.BindByName = true;
        }
    }
}
