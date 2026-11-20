using Inquiry.DependencyInjection;
using Inquiry.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Registers the provider-neutral Inquiry defaults for Aspire applications.
/// </summary>
/// <remarks>
/// The provider registrations, such as <c>AddInquirySqlServer(builder, "name")</c>, ship in the
/// <c>Ignyte.Inquiry.Aspire.*</c> provider packages and call <see cref="AddInquiryAspireDefaults"/>.
/// </remarks>
public static class InquiryAspireExtensions
{
    private static readonly object _defaultsKey = new();

    /// <summary>
    /// Registers Inquiry telemetry, subscribes OpenTelemetry to the Inquiry activity source and meter,
    /// and adds the <c>inquiry</c> health check. A second call adds nothing.
    /// </summary>
    /// <remarks>
    /// Call this after you register a provider from an existing data source. The Aspire provider
    /// registrations call it for you.
    /// </remarks>
    public static IHostApplicationBuilder AddInquiryAspireDefaults(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // A second "inquiry" health check registration makes HealthCheckService throw when it starts.
        if (!builder.Properties.TryAdd(_defaultsKey, true))
        {
            return builder;
        }

        builder.Services.AddInquiryTelemetry();
        builder.Services.AddHealthChecks().AddInquiry();
        builder.Services.AddOpenTelemetry()
            .WithTracing(tracing => tracing.AddSource(InquiryTelemetry.ActivitySourceName))
            .WithMetrics(metrics => metrics.AddMeter(InquiryTelemetry.MeterName));
        return builder;
    }
}
