using Inquiry.Connections;
using Inquiry.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Inquiry.DependencyInjection;

/// <summary>
/// Registers the Inquiry database health check.
/// </summary>
public static class InquiryHealthChecksBuilderExtensions
{
    /// <summary>
    /// Adds a health check that opens a connection through the registered Inquiry connection
    /// factory (exercising the same open path as the pipeline, including configured retry and
    /// failover):
    /// <code>
    /// services.AddHealthChecks().AddInquiry();
    /// </code>
    /// </summary>
    /// <param name="builder">The health checks builder.</param>
    /// <param name="name">The health check name. Defaults to <c>"inquiry"</c>.</param>
    /// <param name="failureStatus">
    /// The status reported when the connection cannot be opened. Defaults to
    /// <see cref="HealthStatus.Unhealthy"/>.
    /// </param>
    /// <param name="tags">Optional tags for filtering health check endpoints.</param>
    public static IHealthChecksBuilder AddInquiry(
        this IHealthChecksBuilder builder,
        string name = "inquiry",
        HealthStatus? failureStatus = null,
        IEnumerable<string>? tags = null)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        return builder.Add(new HealthCheckRegistration(
            name,
            provider => new InquiryHealthCheck(provider.GetRequiredService<IInquiryConnectionFactory>()),
            failureStatus,
            tags));
    }
}
