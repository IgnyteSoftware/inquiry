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
    /// Normalizes bound parameters for Oracle before execution. Two fixups, both because the shared,
    /// dialect-agnostic binder cannot know it is targeting Oracle:
    /// <list type="bullet">
    /// <item><description>Strips the <c>@</c> (or <c>:</c>) sigil the binder prepends to every parameter
    /// name. Oracle's SQL references bind variables as <c>:name</c>, and ODP.NET with
    /// <see cref="OracleCommand.BindByName"/> matches by bare name — it does not reconcile a leading
    /// <c>@</c>, so without this every bound query fails with ORA-50028 ("invalid parameter
    /// binding").</description></item>
    /// <item><description>Converts <see cref="bool"/> values to their <c>0</c>/<c>1</c> numeric form.
    /// Oracle has no BOOLEAN SQL type; Inquiry maps bool columns to <c>NUMBER(1)</c>, and ODP.NET does
    /// not coerce a CLR bool parameter to NUMBER, so binding one fails with ORA-00932 ("inconsistent
    /// datatypes: expected NUMBER got BOOLEAN").</description></item>
    /// </list>
    /// Runs after the pipeline binds parameters, before execution.
    /// </summary>
    public void FinalizeCommand(DbCommand command)
    {
        foreach (DbParameter parameter in command.Parameters)
        {
            var name = parameter.ParameterName;
            if (!string.IsNullOrEmpty(name))
            {
                if (name[0] == '@' || name[0] == ':')
                {
                    name = name.Substring(1);
                }

                // Oracle bind names cannot begin with '_'. OracleSqlBuilder.ParameterName drops the
                // leading underscores the shared generator uses for synthetic paging params, so apply the
                // same trim here to keep the bound name matching the ':name' placeholder under BindByName.
                parameter.ParameterName = name.TrimStart('_');
            }

            if (parameter.Value is bool boolValue)
            {
                // Also reset DbType: the shared binder stamps DbType.Boolean (W4 DbType metadata), and
                // ODP.NET honors that over the value, so converting the value alone still binds a BOOLEAN.
                parameter.Value = boolValue ? 1 : 0;
                parameter.DbType = System.Data.DbType.Int32;
            }
        }

        // ReturnEntity = true ops are emitted (OracleSqlBuilder) as an anonymous PL/SQL block that runs the
        // mutation and OPENs a ref cursor (:rc) over the affected row. ExecuteReader on such a block returns
        // that cursor's reader, so the shared reader pipeline materializes it unchanged — but the OUT ref
        // cursor must be bound here, since the dialect-agnostic binder cannot create an OracleDbType.RefCursor.
        if (command is OracleCommand oracleCommand && IsReturningBlock(oracleCommand.CommandText))
        {
            oracleCommand.Parameters.Add(new OracleParameter("rc", OracleDbType.RefCursor) { Direction = System.Data.ParameterDirection.Output });
        }
    }

    // A returning op is the only SQL Inquiry emits as an anonymous PL/SQL block; normal CRUD never starts
    // with these tokens. Must stay in sync with the leading token of OracleSqlBuilder's returning builders
    // (DECLARE for a generated-key insert, BEGIN otherwise).
    private static bool IsReturningBlock(string commandText)
        => commandText.StartsWith("DECLARE", System.StringComparison.Ordinal)
           || commandText.StartsWith("BEGIN", System.StringComparison.Ordinal);
}
