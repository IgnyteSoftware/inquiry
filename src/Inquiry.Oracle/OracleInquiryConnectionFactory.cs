using Inquiry.Connections;
using Oracle.ManagedDataAccess.Client;
using System.Data.Common;
using System.Globalization;

namespace Inquiry.Oracle;

/// <summary>
/// Opens Oracle connections for the Inquiry request pipeline.
/// </summary>
internal sealed class OracleInquiryConnectionFactory : IInquiryConnectionFactory
{
    private readonly string _connectionString;
    private readonly string? _failoverConnectionString;
    private readonly RetryingConnectionOpener? _retryingOpener;

    private readonly Func<CancellationToken, ValueTask<DbConnection>> _openPrimary;

    /// <summary>
    /// Initializes a new instance of <see cref="OracleInquiryConnectionFactory"/> with default options.
    /// </summary>
    public OracleInquiryConnectionFactory(string connectionString)
        : this(connectionString, new OracleInquiryOptions())
    {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="OracleInquiryConnectionFactory"/>.
    /// </summary>
    public OracleInquiryConnectionFactory(string connectionString, OracleInquiryOptions options)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string cannot be empty.", nameof(connectionString));
        }

        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        _connectionString = connectionString;
        _failoverConnectionString = options.FailoverConnectionString is { } configured
            && !string.Equals(configured, connectionString, StringComparison.Ordinal)
                ? configured
                : null;
        _openPrimary = ct => OpenCoreAsync(_connectionString, ct);

        if (options.Compatibility != OracleCompatibility.None)
        {
            _retryingOpener = new RetryingConnectionOpener(
                new OracleTransientErrorDetector(),
                options.MaxAttempts,
                options.RetryBaseDelay,
                maxDelay: options.RetryMaxDelay);
        }
    }

    /// <inheritdoc />
    public ValueTask<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (_failoverConnectionString is { } failover)
        {
            return FailoverConnectionOpener.OpenAsync(OpenCoreAsync, _connectionString, failover, _retryingOpener, cancellationToken);
        }

        return _retryingOpener is null
            ? OpenCoreAsync(_connectionString, cancellationToken)
            : _retryingOpener.OpenAsync(_openPrimary, cancellationToken);
    }

    private async ValueTask<DbConnection> OpenCoreAsync(string connectionString, CancellationToken cancellationToken)
    {
        var connection = new OracleConnection(connectionString);
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

    // Oracle's FinalizeCommand below strips the '@' sigil from parameter names and converts bools
    // to 0/1 — both on DbCommand. The DbBatch path binds onto DbBatchCommand and bypasses
    // FinalizeCommand entirely, so a future ODP.NET DbBatch implementation would skip those fixups
    // and every batched statement would fail to bind. Oracle therefore takes the sequential path.
    /// <inheritdoc />
    public bool SupportsBatchExecution => false;

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
    /// binding"). Generated leading-underscore names are also mapped to the same collision-resistant
    /// bind name emitted by the Oracle analyzer when the command text contains that generated
    /// placeholder.</description></item>
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

                var safeName = SafeBindName(name);
                parameter.ParameterName = safeName != name && ContainsBindName(command.CommandText, safeName)
                    ? safeName
                    : name;
            }

            if (parameter.DbType == System.Data.DbType.Boolean)
            {
                if (parameter.Value is bool boolValue)
                {
                    parameter.Value = boolValue ? 1 : 0;
                }
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
    // (DECLARE for a generated-key insert, BEGIN otherwise) AND with the synthetic `:rc` OUT ref-cursor
    // bind name. Requiring both gates the auto-bind to generator-emitted SQL: user-authored ad-hoc PL/SQL
    // that happens to start with DECLARE/BEGIN (a `SELECT INTO`, a local-only block, a hand-written
    // procedure call) does not reference `:rc`, so it does not gain a stray OUT parameter that would
    // change its shape (audit P2 #7).
    private static bool IsReturningBlock(string commandText)
        => (commandText.StartsWith("DECLARE", System.StringComparison.Ordinal)
            || commandText.StartsWith("BEGIN", System.StringComparison.Ordinal))
           && commandText.IndexOf(":rc", System.StringComparison.Ordinal) >= 0;

    private static string SafeBindName(string name)
        => !string.IsNullOrEmpty(name) && name[0] == '_'
            ? "inq$" + name.Length.ToString(CultureInfo.InvariantCulture) + "$" + name
            : name;

    private static bool ContainsBindName(string commandText, string bindName)
    {
        var needle = ":" + bindName;
        var start = 0;
        while (true)
        {
            var index = commandText.IndexOf(needle, start, System.StringComparison.Ordinal);
            if (index < 0)
            {
                return false;
            }

            var next = index + needle.Length;
            if (next == commandText.Length || !IsBindNameChar(commandText[next]))
            {
                return true;
            }

            start = index + 1;
        }
    }

    private static bool IsBindNameChar(char c)
        => (c >= 'A' && c <= 'Z')
           || (c >= 'a' && c <= 'z')
           || (c >= '0' && c <= '9')
           || c == '_'
           || c == '$'
           || c == '#';
}
