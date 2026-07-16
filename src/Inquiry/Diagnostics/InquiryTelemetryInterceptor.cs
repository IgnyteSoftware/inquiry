using Inquiry.Commands;
using Inquiry.Interceptors;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Data.Common;
using System.Diagnostics;
using System.Runtime.CompilerServices;

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
internal sealed class InquiryTelemetryInterceptor : IInquiryCommandInterceptor, IInquiryInterceptorActivation
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
    }

    public InquiryTelemetryInterceptor(InquiryTelemetryOptions options, ILoggerFactory? loggerFactory)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = loggerFactory?.CreateLogger("Inquiry.Command") ?? NullLogger.Instance;
    }

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

        var state = new CommandState
        {
            DbSystem = InquiryTelemetry.MapDbSystem(context.Command),
            Operation = OperationName(context.Command.CommandText),
            StartTimestamp = Stopwatch.GetTimestamp(),
        };

        if (InquiryTelemetry.ActivitySource.HasListeners())
        {
            var activity = InquiryTelemetry.ActivitySource.StartActivity(state.Operation, ActivityKind.Client);
            if (activity is not null)
            {
                activity.SetTag("db.system.name", state.DbSystem);
                activity.SetTag("db.operation.name", state.Operation);
                if (_options.RecordCommandText)
                {
                    activity.SetTag("db.query.text", context.Command.CommandText);
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
        var errorType = context.Exception.GetType().FullName ?? context.Exception.GetType().Name;

        InquiryTelemetry.CommandDuration.Record(
            elapsed.TotalSeconds,
            new KeyValuePair<string, object?>("db.system.name", state.DbSystem),
            new KeyValuePair<string, object?>("db.operation.name", state.Operation),
            new KeyValuePair<string, object?>("error.type", errorType));

        if (state.Activity is { } activity)
        {
            activity.SetTag("error.type", errorType);
            activity.SetStatus(ActivityStatusCode.Error, context.Exception.Message);
            activity.Dispose();
        }

        Log.Failed(_logger, context.Exception, state.Operation, elapsed.TotalMilliseconds);
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

        public static void Executing(ILogger logger, string operation, string commandText)
            => ExecutingMessage(logger, operation, commandText, null);

        public static void Executed(ILogger logger, string operation, double elapsedMs, int? recordsAffected)
            => ExecutedMessage(logger, operation, elapsedMs, recordsAffected, null);

        public static void Failed(ILogger logger, Exception exception, string operation, double elapsedMs)
            => FailedMessage(logger, operation, elapsedMs, exception);
    }
}
