using System.Data.Common;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Inquiry.Commands;
using Inquiry.Interceptors;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Inquiry.Diagnostics;

/// <summary>
/// Command interceptor that emits OpenTelemetry-compatible spans (<see cref="ActivitySource"/>
/// "Inquiry"), duration metrics (<see cref="System.Diagnostics.Metrics.Meter"/> "Inquiry"), and
/// <see cref="ILogger"/> messages for every command the pipeline executes. Registered by
/// <c>AddInquiryTelemetry()</c>; when no listener/exporter/logger is attached each path is a no-op.
/// </summary>
/// <remarks>
/// Span and duration boundaries are the pipeline's interceptor notifications: from just before
/// execution to after the result set has been fully consumed (or the failure observed). For a
/// streaming query whose enumeration is abandoned early no completion notification fires, so that
/// span is dropped rather than recorded with a fabricated duration.
/// </remarks>
internal sealed class InquiryTelemetryInterceptor: IInquiryCommandInterceptor, IInquiryInterceptorActivation
{
    private readonly InquiryTelemetryOptions _options;
    private readonly ILogger _logger;

    // Correlates the Executing notification with its Executed/Failed counterpart. The DbCommand
    // instance is stable for the lifetime of one pipeline operation, and entries vanish with the
    // command if a streaming enumeration is abandoned before completion.
    private readonly ConditionalWeakTable<DbCommand, CommandState> _inflight = new();

    private sealed class CommandState
    {
        public Activity? Activity;
        public long StartTimestamp;
        public string DbSystem = "other_sql";
        public string Operation = "";
        public string? Table;
    }

    public InquiryTelemetryInterceptor(InquiryTelemetryOptions options, ILoggerFactory? loggerFactory)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = loggerFactory?.CreateLogger("Inquiry.Command") ?? NullLogger.Instance;
    }

    internal bool RecordCommandText => _options.RecordCommandText;

    public bool IsActive
        => InquiryTelemetry.ActivitySource.HasListeners()
            || InquiryTelemetry.CommandDuration.Enabled
            || _logger.IsEnabled(LogLevel.Debug)
            || _logger.IsEnabled(LogLevel.Error);

    public ValueTask CommandExecutingAsync(InquiryCommandContext context, CancellationToken cancellationToken = default)
    {
        if (!IsActive)
        {
            return ValueTask.CompletedTask;
        }

        var commandText = context.Command.CommandText;
        var state = new CommandState
        {
            DbSystem = InquiryTelemetry.MapDbSystem(context.Command),
            Operation = OperationName(commandText),
            Table = _options.RecordCommandText ? TableName(commandText) : null,
            StartTimestamp = Stopwatch.GetTimestamp(),
        };

        if (InquiryTelemetry.ActivitySource.HasListeners())
        {
            var activity = InquiryTelemetry.ActivitySource.StartActivity(state.Operation, ActivityKind.Client);
            if (activity is not null)
            {
                activity.SetTag("db.system.name", state.DbSystem);
                activity.SetTag("db.operation.name", state.Operation);
                if (state.Table is not null)
                {
                    activity.SetTag("db.collection.name", state.Table);
                }

                if (_options.RecordCommandText)
                {
                    activity.SetTag("db.query.text", commandText);
                }

                state.Activity = activity;
            }
        }

        _inflight.AddOrUpdate(context.Command, state);

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            Log.Executing(_logger, state.Operation, _options.RecordCommandText ? context.Command.CommandText : "(redacted)");
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask CommandExecutedAsync(InquiryCommandExecutedContext context, CancellationToken cancellationToken = default)
    {
        if (!_inflight.TryGetValue(context.Command, out var state))
        {
            return ValueTask.CompletedTask;
        }

        _inflight.Remove(context.Command);
        var elapsed = Stopwatch.GetElapsedTime(state.StartTimestamp);

        InquiryTelemetry.CommandDuration.Record(
            elapsed.TotalSeconds,
            new KeyValuePair<string, object?>("db.system.name", state.DbSystem),
            new KeyValuePair<string, object?>("db.operation.name", state.Operation));

        if (state.Activity is { } activity)
        {
            if (context.RecordsAffected is { } affected)
            {
                activity.SetTag("db.response.affected_rows", affected);
            }

            activity.Dispose();
        }

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            Log.Executed(_logger, state.Operation, elapsed.TotalMilliseconds, context.RecordsAffected);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask CommandFailedAsync(InquiryCommandFailedContext context, CancellationToken cancellationToken = default)
    {
        if (!_inflight.TryGetValue(context.Command, out var state))
        {
            return ValueTask.CompletedTask;
        }

        _inflight.Remove(context.Command);
        var elapsed = Stopwatch.GetElapsedTime(state.StartTimestamp);
        var errorType = InquiryTelemetry.ErrorType(context.Exception);

        InquiryTelemetry.CommandDuration.Record(
            elapsed.TotalSeconds,
            new KeyValuePair<string, object?>("db.system.name", state.DbSystem),
            new KeyValuePair<string, object?>("db.operation.name", state.Operation),
            new KeyValuePair<string, object?>("error.type", errorType));

        if (state.Activity is { } activity)
        {
            InquiryTelemetry.SetError(activity, context.Exception, _options.RecordCommandText);
            activity.Dispose();
        }

        if (_options.RecordCommandText)
        {
            Log.Failed(_logger, context.Exception, state.Operation, elapsed.TotalMilliseconds);
        }
        else if (_logger.IsEnabled(LogLevel.Error))
        {
            // A logged exception prints its provider message, which can quote SQL names.
            var cause = InquiryTelemetry.CancellationCause(context.Exception);
            Log.FailedRedacted(
                _logger,
                state.Operation,
                elapsed.TotalMilliseconds,
                errorType,
                cause is null ? null : InquiryTelemetry.ErrorType(cause));
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>Extracts the leading SQL keyword (SELECT/INSERT/...) as the span/log operation name.</summary>
    private static string OperationName(string commandText)
    {
        var span = commandText.AsSpan().TrimStart();
        var end = 0;
        while (end < span.Length && char.IsAsciiLetter(span[end]))
        {
            end++;
        }

        return end == 0 ? "SQL" : span[..end].ToString().ToUpperInvariant();
    }

    /// <summary>
    /// Best-effort extraction of the target table from common SQL patterns: <c>INSERT [OR x | IGNORE]
    /// INTO t</c>, <c>UPDATE [OR x | TOP (n) [PERCENT]] t</c>, <c>DELETE FROM t</c>, and the first
    /// <c>FROM t</c> of the outer <c>SELECT</c>. Returns the last part of a qualified name.
    /// </summary>
    /// <remarks>
    /// Returns <see langword="null"/> for other statements and for shapes it cannot read with
    /// certainty, such as derived tables, table-valued functions, or unterminated quotes. A missing
    /// tag is better than a wrong one.
    /// </remarks>
    internal static string? TableName(string commandText)
    {
        var sql = commandText.AsSpan();
        var pos = 0;
        var keyword = ReadWord(sql, ref pos);

        if (keyword.Equals("INSERT", StringComparison.OrdinalIgnoreCase))
        {
            if (TryKeyword(sql, ref pos, "OR"))
            {
                if (ReadWord(sql, ref pos).IsEmpty)
                {
                    return null;
                }
            }
            else
            {
                TryKeyword(sql, ref pos, "IGNORE");
            }

            return TryKeyword(sql, ref pos, "INTO") ? ReadTableName(sql, ref pos) : null;
        }

        if (keyword.Equals("UPDATE", StringComparison.OrdinalIgnoreCase))
        {
            if (TryKeyword(sql, ref pos, "OR"))
            {
                if (ReadWord(sql, ref pos).IsEmpty)
                {
                    return null;
                }
            }
            else if (TryKeyword(sql, ref pos, "TOP"))
            {
                pos = SkipTrivia(sql, pos);
                if (pos >= sql.Length || sql[pos] != '(')
                {
                    return null;
                }

                pos = SkipParenthesized(sql, pos);
                if (pos < 0)
                {
                    return null;
                }

                TryKeyword(sql, ref pos, "PERCENT");
            }

            return ReadTableName(sql, ref pos);
        }

        if (keyword.Equals("DELETE", StringComparison.OrdinalIgnoreCase))
        {
            return TryKeyword(sql, ref pos, "FROM") ? ReadTableName(sql, ref pos) : null;
        }

        if (keyword.Equals("SELECT", StringComparison.OrdinalIgnoreCase))
        {
            pos = FindOuterFrom(sql, pos);
            if (pos < 0)
            {
                return null;
            }

            var table = ReadTableName(sql, ref pos);

            // "FROM name(" is a table-valued function, not a table.
            pos = SkipTrivia(sql, pos);
            return pos < sql.Length && sql[pos] == '(' ? null : table;
        }

        return null;
    }

    // Returns the position after the first FROM keyword outside parentheses, quotes, and comments,
    // or -1 when the first statement has none.
    private static int FindOuterFrom(ReadOnlySpan<char> sql, int pos)
    {
        var depth = 0;
        while (pos < sql.Length)
        {
            var next = SkipTrivia(sql, pos);
            if (next != pos)
            {
                pos = next;
                continue;
            }

            var c = sql[pos];
            if (c is '\'' or '"' or '[' or '`')
            {
                pos = SkipDelimited(sql, pos);
                if (pos < 0)
                {
                    return -1;
                }
            }
            else if (IsWordChar(c))
            {
                var word = ReadWord(sql, ref pos);
                if (depth == 0 && word.Equals("FROM", StringComparison.OrdinalIgnoreCase))
                {
                    return pos;
                }
            }
            else
            {
                if (c == '(')
                {
                    depth++;
                }
                else if (c == ')')
                {
                    depth--;
                }
                else if (c == ';' && depth == 0)
                {
                    return -1;
                }

                pos++;
            }
        }

        return -1;
    }

    // Reads a one- or multi-part name and returns its last part, or null when the name is malformed.
    private static string? ReadTableName(ReadOnlySpan<char> sql, ref int pos)
    {
        while (true)
        {
            pos = SkipTrivia(sql, pos);
            var part = ReadNamePart(sql, ref pos);
            if (part is null)
            {
                return null;
            }

            var next = SkipTrivia(sql, pos);
            if (next >= sql.Length || sql[next] != '.')
            {
                return part;
            }

            pos = next + 1;
        }
    }

    private static string? ReadNamePart(ReadOnlySpan<char> sql, ref int pos)
    {
        if (pos >= sql.Length)
        {
            return null;
        }

        var open = sql[pos];
        if (open is '[' or '"' or '`')
        {
            var end = SkipDelimited(sql, pos);
            if (end < 0 || end - pos <= 2)
            {
                return null;
            }

            var close = open == '[' ? ']' : open;
            var raw = sql[(pos + 1)..(end - 1)].ToString();
            pos = end;
            return raw.Replace(new string(close, 2), close.ToString(), StringComparison.Ordinal);
        }

        var word = ReadWord(sql, ref pos);
        return word.IsEmpty ? null : word.ToString();
    }

    // Returns the position after the closing delimiter, or -1 when the quote is not closed.
    // A doubled closing delimiter is an escape, as in [a]]b], "a""b", and 'it''s'.
    private static int SkipDelimited(ReadOnlySpan<char> sql, int pos)
    {
        var close = sql[pos] == '[' ? ']' : sql[pos];
        pos++;
        while (true)
        {
            var offset = sql[pos..].IndexOf(close);
            if (offset < 0)
            {
                return -1;
            }

            pos += offset + 1;
            if (pos < sql.Length && sql[pos] == close)
            {
                pos++;
                continue;
            }

            return pos;
        }
    }

    // Returns the position after the parenthesis that closes the one at pos, or -1.
    private static int SkipParenthesized(ReadOnlySpan<char> sql, int pos)
    {
        var depth = 0;
        while (pos < sql.Length)
        {
            var c = sql[pos];
            if (c is '\'' or '"' or '[' or '`')
            {
                pos = SkipDelimited(sql, pos);
                if (pos < 0)
                {
                    return -1;
                }

                continue;
            }

            pos++;
            if (c == '(')
            {
                depth++;
            }
            else if (c == ')' && --depth == 0)
            {
                return pos;
            }
        }

        return -1;
    }

    private static bool TryKeyword(ReadOnlySpan<char> sql, ref int pos, string keyword)
    {
        var end = pos;
        if (!ReadWord(sql, ref end).Equals(keyword, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        pos = end;
        return true;
    }

    // Skips whitespace and comments, then reads a whole word. pos moves only when a word is read.
    private static ReadOnlySpan<char> ReadWord(ReadOnlySpan<char> sql, ref int pos)
    {
        var start = SkipTrivia(sql, pos);
        var end = start;
        while (end < sql.Length && IsWordChar(sql[end]))
        {
            end++;
        }

        if (end == start)
        {
            return default;
        }

        pos = end;
        return sql[start..end];
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c is '_' or '$' or '#' or '@';

    private static int SkipTrivia(ReadOnlySpan<char> sql, int pos)
    {
        while (pos < sql.Length)
        {
            if (char.IsWhiteSpace(sql[pos]))
            {
                pos++;
            }
            else if (sql[pos..].StartsWith("--", StringComparison.Ordinal))
            {
                var end = sql[pos..].IndexOf('\n');
                pos = end < 0 ? sql.Length : pos + end + 1;
            }
            else if (sql[pos..].StartsWith("/*", StringComparison.Ordinal))
            {
                var end = sql[(pos + 2)..].IndexOf("*/", StringComparison.Ordinal);
                pos = end < 0 ? sql.Length : pos + 2 + end + 2;
            }
            else
            {
                break;
            }
        }

        return pos;
    }

    private static class Log
    {
        private static readonly Action<ILogger, string, string, Exception?> ExecutingMessage =
            LoggerMessage.Define<string, string>(
                LogLevel.Debug,
                new EventId(1, "InquiryCommandExecuting"),
                "Executing {Operation}: {CommandText}");

        private static readonly Action<ILogger, string, double, int?, Exception?> ExecutedMessage =
            LoggerMessage.Define<string, double, int?>(
                LogLevel.Debug,
                new EventId(2, "InquiryCommandExecuted"),
                "Executed {Operation} in {ElapsedMs:0.###} ms ({RecordsAffected} rows affected)");

        private static readonly Action<ILogger, string, double, Exception?> FailedMessage =
            LoggerMessage.Define<string, double>(
                LogLevel.Error,
                new EventId(3, "InquiryCommandFailed"),
                "Failed {Operation} after {ElapsedMs:0.###} ms");

        private static readonly Action<ILogger, string, double, string, Exception?> FailedRedactedMessage =
            LoggerMessage.Define<string, double, string>(
                LogLevel.Error,
                new EventId(3, "InquiryCommandFailed"),
                "Failed {Operation} after {ElapsedMs:0.###} ms with {ErrorType}");

        private static readonly Action<ILogger, string, double, string, string, Exception?> FailedRedactedWithCauseMessage =
            LoggerMessage.Define<string, double, string, string>(
                LogLevel.Error,
                new EventId(3, "InquiryCommandFailed"),
                "Failed {Operation} after {ElapsedMs:0.###} ms with {ErrorType} caused by {CauseType}");

        public static void Executing(ILogger logger, string operation, string commandText)
            => ExecutingMessage(logger, operation, commandText, null);

        public static void Executed(ILogger logger, string operation, double elapsedMs, int? recordsAffected)
            => ExecutedMessage(logger, operation, elapsedMs, recordsAffected, null);

        public static void Failed(ILogger logger, Exception exception, string operation, double elapsedMs)
            => FailedMessage(logger, operation, elapsedMs, exception);

        public static void FailedRedacted(ILogger logger, string operation, double elapsedMs, string errorType, string? causeType)
        {
            if (causeType is null)
            {
                FailedRedactedMessage(logger, operation, elapsedMs, errorType, null);
            }
            else
            {
                FailedRedactedWithCauseMessage(logger, operation, elapsedMs, errorType, causeType, null);
            }
        }
    }
}
