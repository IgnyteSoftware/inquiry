using Inquiry.Commands;
using Microsoft.Extensions.Logging;
using System.Data.Common;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Inquiry.Interceptors;

/// <summary>
/// Logs a warning when a command's execution time meets or exceeds a threshold. Duration is
/// measured from <see cref="IInquiryCommandInterceptor.CommandExecutingAsync"/> to
/// <see cref="IInquiryCommandInterceptor.CommandExecutedAsync"/> — the provider round trip, not
/// result enumeration. The log message carries the duration and the command text; parameter
/// values are never logged (same posture as Inquiry's telemetry).
/// </summary>
public sealed class SlowQueryLoggingInterceptor : IInquiryCommandInterceptor
{
    private readonly ILogger<SlowQueryLoggingInterceptor> _logger;
    private readonly TimeSpan _threshold;

    // Keyed by the live DbCommand so the executing/executed pair correlates without any
    // per-command allocation surviving beyond the command's own lifetime.
    private readonly ConditionalWeakTable<DbCommand, StrongBox<long>> _startTimestamps = new();

    /// <summary>Initializes the interceptor with the slow threshold.</summary>
    public SlowQueryLoggingInterceptor(ILogger<SlowQueryLoggingInterceptor> logger, TimeSpan threshold)
    {
        if (threshold <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(threshold), "Threshold must be positive.");
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _threshold = threshold;
    }

    /// <inheritdoc />
    public ValueTask CommandExecutingAsync(InquiryCommandContext context, CancellationToken cancellationToken = default)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));
        _startTimestamps.AddOrUpdate(context.Command, new StrongBox<long>(Stopwatch.GetTimestamp()));
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask CommandExecutedAsync(InquiryCommandExecutedContext context, CancellationToken cancellationToken = default)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));
        if (_startTimestamps.TryGetValue(context.Command, out var start))
        {
            _startTimestamps.Remove(context.Command);
            var elapsed = Stopwatch.GetElapsedTime(start.Value);
            if (elapsed >= _threshold)
            {
                _logger.LogWarning(
                    "Inquiry command took {ElapsedMilliseconds} ms (threshold {ThresholdMilliseconds} ms): {CommandText}",
                    (long)elapsed.TotalMilliseconds,
                    (long)_threshold.TotalMilliseconds,
                    context.Command.CommandText);
            }
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask CommandFailedAsync(InquiryCommandFailedContext context, CancellationToken cancellationToken = default)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));
        // Failures surface through the exception (and telemetry); just drop the correlation entry.
        _startTimestamps.Remove(context.Command);
        return ValueTask.CompletedTask;
    }
}
