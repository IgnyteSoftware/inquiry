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

    /// <summary>
    /// Strips the dialect-agnostic <c>@</c> (or <c>:</c>) sigil the shared parameter binder prepends to
    /// every parameter name. Oracle's SQL references bind variables as <c>:name</c>, and ODP.NET with
    /// <see cref="OracleCommand.BindByName"/> matches a parameter to a placeholder by its bare name — it
    /// does not reconcile a leading <c>@</c>, so without this fixup every bound query fails with ORA-50028
    /// ("invalid parameter binding"). Runs after the pipeline binds parameters, before execution.
    /// </summary>
    public void FinalizeCommand(DbCommand command)
    {
        foreach (DbParameter parameter in command.Parameters)
        {
            var name = parameter.ParameterName;
            if (!string.IsNullOrEmpty(name) && (name[0] == '@' || name[0] == ':'))
            {
                parameter.ParameterName = name.Substring(1);
            }
        }
    }
}
