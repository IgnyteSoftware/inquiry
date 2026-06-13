using System;
using System.Threading;
using System.Threading.Tasks;
using Inquiry.Commands;
using Microsoft.Extensions.Logging;

namespace Inquiry.Interceptors;

/// <summary>
/// A dev-time N+1 detector (Rails bullet / prosopite analog). Within an
/// <see cref="InquiryNPlusOneScope"/>, it counts how often each distinct command text executes and logs a
/// warning the moment one reaches the configured threshold — the signature of an N+1: the same
/// parameterized SQL run once per item in a loop. Because Inquiry parameterizes values, the command text
/// is identical across those executions even though the parameters differ, so the repeats fingerprint
/// together. Outside any scope it does nothing. Command text is logged; parameter values never are (same
/// posture as the other interceptors).
/// </summary>
public sealed class NPlusOneDetectionInterceptor : IInquiryCommandInterceptor
{
    private readonly ILogger<NPlusOneDetectionInterceptor> _logger;
    private readonly int _threshold;

    /// <summary>Initializes the detector with the repeat threshold (at least 2).</summary>
    public NPlusOneDetectionInterceptor(ILogger<NPlusOneDetectionInterceptor> logger, int threshold)
    {
        if (threshold < 2) throw new ArgumentOutOfRangeException(nameof(threshold), "Threshold must be at least 2.");
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _threshold = threshold;
    }

    /// <inheritdoc />
    public ValueTask CommandExecutingAsync(InquiryCommandContext context, CancellationToken cancellationToken = default)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));

        var scope = InquiryNPlusOneScope.Current;
        if (scope is null)
        {
            return ValueTask.CompletedTask;
        }

        var sql = Fingerprint(context.Command.CommandText);
        var count = scope.Counts.AddOrUpdate(sql, 1, static (_, current) => current + 1);

        // Warn exactly once per statement — when its count first reaches the threshold.
        if (count == _threshold)
        {
            _logger.LogWarning(
                "Possible N+1: the same SQL executed {Count} times within this detection scope (different parameters each time): {CommandText}",
                count,
                sql);
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Strips a trailing block comment so a per-execution tag (e.g. the sqlcommenter
    /// <c>traceparent</c>) doesn't make otherwise-identical statements fingerprint apart — keeping
    /// detection robust regardless of interceptor registration order.
    /// </summary>
    private static string Fingerprint(string commandText)
    {
        var trimmed = commandText.TrimEnd();
        if (trimmed.EndsWith("*/", StringComparison.Ordinal))
        {
            var open = trimmed.LastIndexOf("/*", StringComparison.Ordinal);
            if (open >= 0)
            {
                return trimmed.Substring(0, open).TrimEnd();
            }
        }

        return commandText;
    }
}
