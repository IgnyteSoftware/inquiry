using Inquiry.Commands;
using Microsoft.Extensions.Logging;

namespace Inquiry.Interceptors;

/// <summary>
/// Detects N+1 query patterns by counting how many times the same SQL fingerprint executes
/// within an <see cref="InquiryNPlusOneScope"/>. Logs a warning the first time a fingerprint
/// reaches the configured threshold. Command text is logged; parameter values never are.
/// </summary>
public sealed class NPlusOneDetectionInterceptor : IInquiryCommandInterceptor
{
    private readonly ILogger<NPlusOneDetectionInterceptor> _logger;
    private readonly int _threshold;

    /// <summary>Initializes the interceptor.</summary>
    /// <param name="logger">Logger for warning messages.</param>
    /// <param name="threshold">The repeat count at which a warning fires (must be at least 2).</param>
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
            return ValueTask.CompletedTask;

        var fingerprint = StripTrailingComment(context.Command.CommandText);
        var count = scope.Counts.AddOrUpdate(fingerprint, 1, static (_, c) => c + 1);

        if (count == _threshold && scope.Warned.TryAdd(fingerprint, 0))
        {
            _logger.LogWarning(
                "Possible N+1: the same SQL executed {Count} times within this detection scope: {CommandText}",
                count,
                fingerprint);
        }

        return ValueTask.CompletedTask;
    }

    private static string StripTrailingComment(string text)
    {
        if (!text.EndsWith("*/", StringComparison.Ordinal))
            return text;

        var start = text.LastIndexOf("/*", StringComparison.Ordinal);
        if (start < 0)
            return text;

        return text[..start].TrimEnd();
    }
}
