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
        var rewriteText = command.CommandType != System.Data.CommandType.StoredProcedure;
        List<BindRename>? renames = null;
        var logicalNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var providerNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (DbParameter parameter in command.Parameters)
        {
            if (!string.IsNullOrEmpty(parameter.ParameterName))
            {
                var parameterName = parameter.ParameterName;
                var logicalName = parameterName[0] == '@' || parameterName[0] == ':'
                    ? parameterName.Substring(1)
                    : parameterName;
                if (!logicalNames.Add(logicalName))
                {
                    throw new InvalidOperationException($"Oracle parameter names must be unique ignoring case; '{logicalName}' is duplicated.");
                }

                if (rewriteText)
                {
                    // Generator-emitted Oracle SQL already uses a safe encoded token. All other
                    // logical names are encoded even when their raw text happens to resemble our
                    // encoded namespace; Encode itself is deliberately not idempotent.
                    var generatedName = OracleBindName.IsEncoded(logicalName)
                        && ContainsBindToken(command.CommandText, logicalName);
                    var safeName = generatedName ? logicalName : OracleBindName.Encode(logicalName);
                    if (!providerNames.Add(safeName))
                    {
                        throw new InvalidOperationException(
                            $"Oracle parameter names '{logicalName}' and another command parameter resolve to the same provider bind '{safeName}'.");
                    }

                    // Whether the encoded target token already appears elsewhere is irrelevant:
                    // every raw occurrence of this logical token still has to be rewritten.
                    if (!generatedName && ContainsBindToken(command.CommandText, logicalName))
                    {
                        (renames ??= new List<BindRename>()).Add(new BindRename(logicalName, safeName));
                    }

                    parameter.ParameterName = safeName;
                }
                else
                {
                    // Stored-procedure parameters are formal names, not SQL bind tokens. Preserve
                    // their spelling and only remove Inquiry's transport sigil.
                    if (!providerNames.Add(logicalName))
                    {
                        throw new InvalidOperationException($"Oracle stored-procedure formal parameter '{logicalName}' is duplicated ignoring case.");
                    }
                    parameter.ParameterName = logicalName;
                }
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

        if (renames is { Count: > 0 })
            command.CommandText = RewriteBindTokens(command.CommandText, renames);

        // ReturnEntity = true ops are emitted (OracleSqlBuilder) as an anonymous PL/SQL block that runs the
        // mutation and OPENs a ref cursor (:rc) over the affected row. ExecuteReader on such a block returns
        // that cursor's reader, so the shared reader pipeline materializes it unchanged — but the OUT ref
        // cursor must be bound here, since the dialect-agnostic binder cannot create an OracleDbType.RefCursor.
        if (command is OracleCommand oracleCommand && IsReturningBlock(oracleCommand.CommandText) && !oracleCommand.Parameters.Contains("rc"))
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
           && ContainsBindToken(commandText, "rc");

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

    private static string RewriteBindTokens(string commandText, List<BindRename> renames)
    {
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

                for (var r = 0; r < renames.Count; r++)
                {
                    var rename = renames[r];
                    if (end - nameStart != rename.Original.Length
                        || string.Compare(commandText, nameStart, rename.Original, 0, rename.Original.Length, StringComparison.OrdinalIgnoreCase) != 0)
                    {
                        continue;
                    }

                    rewritten ??= new StringBuilder(commandText.Length + 16);
                    rewritten.Append(commandText, copiedThrough, tokenStart - copiedThrough);
                    rewritten.Append(':').Append(rename.Safe);
                    copiedThrough = end;
                    break;
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

    private readonly struct BindRename
    {
        public BindRename(string original, string safe) => (Original, Safe) = (original, safe);
        public string Original { get; }
        public string Safe { get; }
    }
}
