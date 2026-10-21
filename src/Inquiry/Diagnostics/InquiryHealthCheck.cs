using Inquiry.Connections;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Inquiry.Diagnostics;

/// <summary>
/// Health check that opens (and immediately disposes) a connection through the registered
/// <see cref="IInquiryConnectionFactory"/>, exercising the same open path the pipeline uses —
/// including any configured retry and failover. Registered via <c>AddInquiry()</c> on
/// <c>IHealthChecksBuilder</c>.
/// </summary>
internal sealed class InquiryHealthCheck : IHealthCheck
{
    private readonly IInquiryConnectionFactory _connectionFactory;

    public InquiryHealthCheck(IInquiryConnectionFactory connectionFactory)
        => _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            return HealthCheckResult.Healthy("Database connection opened successfully.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new HealthCheckResult(
                context.Registration.FailureStatus,
                "Opening a database connection failed.",
                exception);
        }
    }
}
