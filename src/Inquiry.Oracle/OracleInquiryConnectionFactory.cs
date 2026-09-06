using Inquiry.Commands;
using Inquiry.Connections;
using Inquiry.Oracle.Shared;
using Oracle.ManagedDataAccess.Client;
using System.Data.Common;
using System.Text;

namespace Inquiry.Oracle;

/// <summary>
/// Opens Oracle connections for the Inquiry request pipeline.
/// </summary>
internal sealed class OracleInquiryConnectionFactory : IInquiryConnectionFactory
{
    private readonly DbDataSource? _dataSource;
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
    /// Initializes a new instance of <see cref="OracleInquiryConnectionFactory"/> that opens
    /// connections from an externally owned data source.
    /// </summary>
    public OracleInquiryConnectionFactory(DbDataSource dataSource)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _connectionString = dataSource.ConnectionString;
        _openPrimary = dataSource.OpenConnectionAsync;
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
        if (_dataSource is not null)
        {
            return _dataSource.OpenConnectionAsync(cancellationToken);
        }

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
    // FinalizeCommand entirely, so a future ODP.NET DbBatch implementation would skip those fixups.
    // Generated batch mutations use ODP.NET array binding on a normal DbCommand instead.
    /// <inheritdoc />
    public bool SupportsBatchExecution => false;

    /// <inheritdoc />
    public InquiryBatchExecutionMode BatchExecutionMode => InquiryBatchExecutionMode.ArrayBinding;

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
        var returningBlock = HasReturningBlockPrefix(command.CommandText);
        if (command.CommandType == System.Data.CommandType.StoredProcedure)
        {
            FinalizeStoredProcedure(command);
            AddRefCursor(command, returningBlock && ContainsBindToken(command.CommandText, "rc"));
            return;
        }

        switch (command.Parameters.Count)
        {
            case 0:
                AddRefCursor(command, returningBlock && ContainsBindToken(command.CommandText, "rc"));
                return;
            case 1:
                FinalizeSingleParameter(command, returningBlock);
                return;
        }

        FinalizeMultipleParameters(command, returningBlock);
    }

    private static void FinalizeStoredProcedure(DbCommand command)
    {
        for (var index = 0; index < command.Parameters.Count; index++)
        {
            var parameter = command.Parameters[index];
            NormalizeBoolean(parameter);
            if (string.IsNullOrEmpty(parameter.ParameterName)) continue;

            var logicalName = GetLogicalName(parameter.ParameterName);
            for (var previous = 0; previous < index; previous++)
            {
                var previousName = command.Parameters[previous].ParameterName;
                if (!string.IsNullOrEmpty(previousName)
                    && string.Equals(previousName, logicalName, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Oracle parameter names must be unique ignoring case; '{logicalName}' is duplicated.");
                }
            }

            // Stored-procedure parameters are formal names, not SQL bind tokens. Preserve
            // their spelling and only remove Inquiry's transport sigil.
            parameter.ParameterName = logicalName;
        }
    }

    private static void FinalizeSingleParameter(DbCommand command, bool returningBlock)
    {
        var parameter = command.Parameters[0];
        NormalizeBoolean(parameter);

        if (string.IsNullOrEmpty(parameter.ParameterName))
        {
            AddRefCursor(command, returningBlock && ContainsBindToken(command.CommandText, "rc"));
            return;
        }

        var logicalName = GetLogicalName(parameter.ParameterName);
        var encoded = OracleBindName.IsEncoded(logicalName);
        var safeName = encoded ? logicalName : OracleBindName.Encode(logicalName);
        command.CommandText = RewriteSingleBindToken(
            command.CommandText,
            logicalName,
            safeName,
            !encoded,
            returningBlock,
            out var tokenFound,
            out var containsRefCursor);

        // An encoded logical name is trusted only when the SQL contains that generated token.
        parameter.ParameterName = encoded && !tokenFound ? OracleBindName.Encode(logicalName) : safeName;
        AddRefCursor(command, containsRefCursor);
    }

    private static void FinalizeMultipleParameters(DbCommand command, bool returningBlock)
    {
        var bindings = new BindParameter[command.Parameters.Count];
        var bindingCount = 0;

        for (var index = 0; index < command.Parameters.Count; index++)
        {
            var parameter = command.Parameters[index];
            NormalizeBoolean(parameter);
            if (string.IsNullOrEmpty(parameter.ParameterName)) continue;

            var logicalName = GetLogicalName(parameter.ParameterName);
            for (var previous = 0; previous < bindingCount; previous++)
            {
                if (string.Equals(bindings[previous].Logical, logicalName, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Oracle parameter names must be unique ignoring case; '{logicalName}' is duplicated.");
                }
            }

            bindings[bindingCount++] = new BindParameter(parameter, logicalName);
        }

        var rewrittenText = RewriteBindTokens(
            command.CommandText,
            bindings.AsSpan(0, bindingCount),
            returningBlock,
            out var containsRefCursor);

        for (var index = 0; index < bindingCount; index++)
        {
            ref var binding = ref bindings[index];
            if (binding.Encoded && !binding.Found)
            {
                binding.Safe = OracleBindName.Encode(binding.Logical);
            }

            for (var previous = 0; previous < index; previous++)
            {
                if (string.Equals(bindings[previous].Safe, binding.Safe, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Oracle parameter names '{binding.Logical}' and another command parameter resolve to the same provider bind '{binding.Safe}'.");
                }
            }
        }

        for (var index = 0; index < bindingCount; index++)
        {
            bindings[index].Parameter.ParameterName = bindings[index].Safe;
        }
        command.CommandText = rewrittenText;
        AddRefCursor(command, containsRefCursor);
    }

    private static string GetLogicalName(string parameterName)
        => parameterName[0] == '@' || parameterName[0] == ':'
            ? parameterName.Substring(1)
            : parameterName;

    private static void NormalizeBoolean(DbParameter parameter)
    {
        if (parameter.DbType != System.Data.DbType.Boolean) return;
        if (parameter.Value is bool boolValue)
        {
            parameter.Value = boolValue ? 1 : 0;
        }
        parameter.DbType = System.Data.DbType.Int32;
    }

    private static void AddRefCursor(DbCommand command, bool containsRefCursor)
    {
        // Returning mutations are emitted (OracleSqlBuilder) as an anonymous PL/SQL block that runs the
        // mutation and OPENs a ref cursor (:rc) over the affected row. ExecuteReader on such a block returns
        // that cursor's reader, so the shared reader pipeline materializes it unchanged — but the OUT ref
        // cursor must be bound here, since the dialect-agnostic binder cannot create an OracleDbType.RefCursor.
        if (containsRefCursor && command is OracleCommand oracleCommand && !oracleCommand.Parameters.Contains("rc"))
        {
            oracleCommand.Parameters.Add(new OracleParameter("rc", OracleDbType.RefCursor) { Direction = System.Data.ParameterDirection.Output });
        }
    }

    // A returning op is the only SQL Inquiry emits as an anonymous PL/SQL block; normal CRUD never starts
    // with these tokens. The prefix must stay in sync with OracleSqlBuilder's returning builders (DECLARE
    // for a generated-key insert, BEGIN otherwise). Callers also require the synthetic `:rc` OUT bind so
    // user-authored blocks do not gain a stray parameter that changes their shape (audit P2 #7).
    private static bool HasReturningBlockPrefix(string commandText)
        => (commandText.StartsWith("DECLARE", System.StringComparison.Ordinal)
            || commandText.StartsWith("BEGIN", System.StringComparison.Ordinal));

    private static bool ContainsBindToken(string commandText, string bindName)
    {
        for (var i = 0; i < commandText.Length;)
        {
            if (TrySkipQuotedOrComment(commandText, ref i)) continue;

            if ((commandText[i] == ':' || commandText[i] == '@')
                && !(commandText[i] == '@' && IsDatabaseLinkAtSign(commandText, i)))
            {
                var start = i + 1;
                var end = start;
                while (end < commandText.Length)
                {
                    var width = BindNameCharWidth(commandText, end);
                    if (width == 0) break;
                    end += width;
                }
                if (end - start == bindName.Length
                    && string.Compare(commandText, start, bindName, 0, bindName.Length, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    return true;
                }
                i = end;
                continue;
            }

            i++;
        }

        return false;
    }

    private static string RewriteSingleBindToken(
        string commandText,
        string logicalName,
        string safeName,
        bool rewrite,
        bool returningBlock,
        out bool tokenFound,
        out bool containsRefCursor)
    {
        tokenFound = false;
        containsRefCursor = false;
        StringBuilder? rewritten = null;
        var copiedThrough = 0;
        for (var i = 0; i < commandText.Length;)
        {
            if (TrySkipQuotedOrComment(commandText, ref i)) continue;

            if ((commandText[i] == ':' || commandText[i] == '@')
                && !(commandText[i] == '@' && IsDatabaseLinkAtSign(commandText, i)))
            {
                var tokenStart = i;
                var nameStart = i + 1;
                var end = nameStart;
                while (end < commandText.Length)
                {
                    var width = BindNameCharWidth(commandText, end);
                    if (width == 0) break;
                    end += width;
                }

                var matched = TokenEquals(commandText, nameStart, end, logicalName);
                tokenFound |= matched;
                if (matched && rewrite)
                {
                    rewritten ??= new StringBuilder(commandText.Length + 16);
                    rewritten.Append(commandText, copiedThrough, tokenStart - copiedThrough);
                    rewritten.Append(':').Append(safeName);
                    copiedThrough = end;
                }
                else if (returningBlock && TokenEquals(commandText, nameStart, end, "rc"))
                {
                    containsRefCursor = true;
                }

                i = end;
                continue;
            }

            i++;
        }

        if (rewritten is null) return commandText;
        rewritten.Append(commandText, copiedThrough, commandText.Length - copiedThrough);
        return rewritten.ToString();
    }

    private static string RewriteBindTokens(
        string commandText,
        Span<BindParameter> bindings,
        bool returningBlock,
        out bool containsRefCursor)
    {
        containsRefCursor = false;
        StringBuilder? rewritten = null;
        var copiedThrough = 0;
        for (var i = 0; i < commandText.Length;)
        {
            if (TrySkipQuotedOrComment(commandText, ref i)) continue;

            if ((commandText[i] == ':' || commandText[i] == '@')
                && !(commandText[i] == '@' && IsDatabaseLinkAtSign(commandText, i)))
            {
                var tokenStart = i;
                var nameStart = i + 1;
                var end = nameStart;
                while (end < commandText.Length)
                {
                    var width = BindNameCharWidth(commandText, end);
                    if (width == 0) break;
                    end += width;
                }

                var rewrittenToken = false;
                for (var bindingIndex = 0; bindingIndex < bindings.Length; bindingIndex++)
                {
                    ref var binding = ref bindings[bindingIndex];
                    if (!TokenEquals(commandText, nameStart, end, binding.Logical)) continue;

                    binding.Found = true;
                    if (!binding.Encoded)
                    {
                        rewritten ??= new StringBuilder(commandText.Length + 16);
                        rewritten.Append(commandText, copiedThrough, tokenStart - copiedThrough);
                        rewritten.Append(':').Append(binding.Safe);
                        copiedThrough = end;
                        rewrittenToken = true;
                    }
                    break;
                }

                if (!rewrittenToken && returningBlock && TokenEquals(commandText, nameStart, end, "rc"))
                {
                    containsRefCursor = true;
                }

                i = end;
                continue;
            }

            i++;
        }

        if (rewritten is null) return commandText;
        rewritten.Append(commandText, copiedThrough, commandText.Length - copiedThrough);
        return rewritten.ToString();
    }

    private static bool TokenEquals(string commandText, int start, int end, string expected)
        => end - start == expected.Length
           && string.Compare(commandText, start, expected, 0, expected.Length, StringComparison.OrdinalIgnoreCase) == 0;

    private static bool TrySkipQuotedOrComment(string text, ref int index)
    {
        var c = text[index];
        if (c == '-' && index + 1 < text.Length && text[index + 1] == '-')
        {
            index += 2;
            while (index < text.Length && text[index] != '\r' && text[index] != '\n') index++;
            return true;
        }

        if (c == '/' && index + 1 < text.Length && text[index + 1] == '*')
        {
            var close = text.IndexOf("*/", index + 2, System.StringComparison.Ordinal);
            index = close < 0 ? text.Length : close + 2;
            return true;
        }

        if (c == '\'' || c == '"')
        {
            var quote = c;
            index++;
            while (index < text.Length)
            {
                if (text[index++] != quote) continue;
                if (index < text.Length && text[index] == quote) { index++; continue; }
                break;
            }
            return true;
        }

        var quotePrefixLength = (c == 'q' || c == 'Q') && index + 2 < text.Length && text[index + 1] == '\''
            ? 2
            : (c == 'n' || c == 'N') && index + 3 < text.Length
                && (text[index + 1] == 'q' || text[index + 1] == 'Q') && text[index + 2] == '\''
                    ? 3
                    : 0;
        if (quotePrefixLength != 0)
        {
            var opener = text[index + quotePrefixLength];
            var closer = opener switch { '[' => ']', '{' => '}', '(' => ')', '<' => '>', _ => opener };
            index += quotePrefixLength + 1;
            while (index + 1 < text.Length)
            {
                if (text[index] == closer && text[index + 1] == '\'') { index += 2; return true; }
                index++;
            }
            index = text.Length;
            return true;
        }

        return false;
    }

    private static bool IsDatabaseLinkAtSign(string text, int index)
    {
        if (index == 0) return false;
        var previous = text[index - 1];
        if (previous == '"' || previous is '_' or '$' or '#' || char.IsLetterOrDigit(previous)
            || char.IsLowSurrogate(previous))
        {
            return true;
        }
        var category = char.GetUnicodeCategory(previous);
        return category is System.Globalization.UnicodeCategory.NonSpacingMark
            or System.Globalization.UnicodeCategory.SpacingCombiningMark;
    }

    private static int BindNameCharWidth(string text, int index)
    {
        if (!Rune.TryGetRuneAt(text, index, out var rune)) return 0;
        if (rune.Value is '_' or '$' or '#') return rune.Utf16SequenceLength;
        if (Rune.IsLetterOrDigit(rune)) return rune.Utf16SequenceLength;
        var category = Rune.GetUnicodeCategory(rune);
        return category is System.Globalization.UnicodeCategory.NonSpacingMark
            or System.Globalization.UnicodeCategory.SpacingCombiningMark
            ? rune.Utf16SequenceLength
            : 0;
    }

    private struct BindParameter
    {
        public BindParameter(DbParameter parameter, string logical)
        {
            Parameter = parameter;
            Logical = logical;
            Encoded = OracleBindName.IsEncoded(logical);
            Safe = Encoded ? logical : OracleBindName.Encode(logical);
            Found = false;
        }

        public DbParameter Parameter { get; }
        public string Logical { get; }
        public bool Encoded { get; }
        public string Safe { get; set; }
        public bool Found { get; set; }
    }
}
