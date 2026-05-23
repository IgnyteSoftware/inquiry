using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Inquiry;

public sealed class LoggingInquiryMiddleware : IInquiryMiddleware
{
    private readonly ILogger<LoggingInquiryMiddleware> _logger;
    private readonly InquiryLoggingOptions _options;

    public LoggingInquiryMiddleware(ILogger<LoggingInquiryMiddleware> logger)
        : this(logger, new InquiryLoggingOptions())
    {
    }

    public LoggingInquiryMiddleware(ILogger<LoggingInquiryMiddleware> logger, InquiryOptions options)
        : this(logger, options.Logging)
    {
    }

    public LoggingInquiryMiddleware(ILogger<LoggingInquiryMiddleware> logger, InquiryLoggingOptions options)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async ValueTask<InquiryResponse> InvokeAsync(
        InquiryRequestContext context,
        InquiryRequestDelegate next,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            if (_options.EnableCommandLogging && !string.IsNullOrWhiteSpace(context.CommandText))
            {
                _logger.LogDebug("Inquiry SQL for {Operation}: {CommandText}", context.Operation, context.CommandText);
            }

            if (_options.EnableParameterLogging && context.Parameters.Count > 0)
            {
                var parameters = _options.EnableSensitiveDataLogging
                    ? context.Parameters
                    : context.Parameters.ToDictionary(pair => pair.Key, _ => (object?)"<redacted>", StringComparer.OrdinalIgnoreCase);
                _logger.LogDebug("Inquiry parameters for {Operation}: {@Parameters}", context.Operation, parameters);
            }

            var response = await next(context).ConfigureAwait(false);
            stopwatch.Stop();
            var elapsed = stopwatch.Elapsed;
            var level = _options.SlowQueryThreshold is not null && elapsed >= _options.SlowQueryThreshold.Value
                ? LogLevel.Warning
                : LogLevel.Information;

            _logger.Log(
                level,
                "Inquiry {Operation} completed in {ElapsedMs} ms with {RowsAffected} rows affected",
                context.Operation,
                elapsed.TotalMilliseconds,
                response.RowsAffected);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Inquiry {Operation} failed", context.Operation);
            throw;
        }
    }
}
