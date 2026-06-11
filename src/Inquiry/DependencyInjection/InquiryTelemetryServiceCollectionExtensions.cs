using Inquiry.Diagnostics;
using Inquiry.Interceptors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Inquiry.DependencyInjection;

/// <summary>
/// Registers the optional Inquiry telemetry layer (OpenTelemetry tracing, metrics, and
/// <see cref="ILogger"/> logging).
/// </summary>
public static class InquiryTelemetryServiceCollectionExtensions
{
    /// <summary>
    /// Adds the telemetry command interceptor: spans on the <c>"Inquiry"</c>
    /// <see cref="System.Diagnostics.ActivitySource"/>, a <c>db.client.operation.duration</c>
    /// histogram on the <c>"Inquiry"</c> <see cref="System.Diagnostics.Metrics.Meter"/>, and
    /// per-command log messages on the <c>"Inquiry.Command"</c> logger category (when logging is
    /// configured). Opt-in: without this call the pipeline carries zero telemetry overhead.
    /// See <see cref="InquiryTelemetry"/> for the OpenTelemetry subscription snippet.
    /// </summary>
    public static IServiceCollection AddInquiryTelemetry(
        this IServiceCollection services,
        Action<InquiryTelemetryOptions>? configure = null)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        var options = new InquiryTelemetryOptions();
        configure?.Invoke(options);

        services.AddSingleton<IInquiryCommandInterceptor>(
            provider => new InquiryTelemetryInterceptor(options, provider.GetService<ILoggerFactory>()));
        return services;
    }
}
