using Inquiry.Interceptors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Inquiry.DependencyInjection;

/// <summary>
/// Registration helpers for the ready-made interceptors in the <c>Inquiry.Interceptors</c>
/// companion package. Each interceptor is opt-in and registers as one more
/// <see cref="Inquiry.Interceptors.IInquiryCommandInterceptor"/> alongside any others
/// (telemetry, custom, testing recorders).
/// </summary>
public static class InquiryInterceptorsServiceCollectionExtensions
{
    /// <summary>
    /// Logs a warning whenever a command takes at least <paramref name="threshold"/>
    /// (default 1 second), measured executing → executed — for queries that includes result
    /// reading and materialization, not just the provider round trip. Command text is logged;
    /// parameter values never are.
    /// </summary>
    public static IServiceCollection AddInquirySlowQueryLogging(this IServiceCollection services, TimeSpan? threshold = null)
    {
        if (services is null) throw new ArgumentNullException(nameof(services));

        services.AddSingleton<IInquiryCommandInterceptor>(provider => new SlowQueryLoggingInterceptor(
            provider.GetRequiredService<ILogger<SlowQueryLoggingInterceptor>>(),
            threshold ?? TimeSpan.FromSeconds(1)));
        return services;
    }

    /// <summary>
    /// Appends a sqlcommenter-style comment (<c>application</c> + W3C <c>traceparent</c>) to each
    /// command so database-side tooling correlates statements back to the issuing trace. Tagged
    /// text varies per trace, which defeats server-side prepared-statement reuse for tagged
    /// commands — see the Interceptors article for the trade-off.
    /// </summary>
    public static IServiceCollection AddInquirySqlCommenter(this IServiceCollection services, string? applicationName = null)
    {
        if (services is null) throw new ArgumentNullException(nameof(services));

        services.AddSingleton<IInquiryCommandInterceptor>(new SqlCommenterInterceptor(applicationName));
        return services;
    }

    /// <summary>
    /// Detects N+1 query patterns within an <see cref="InquiryNPlusOneScope"/>. When the same SQL
    /// fingerprint executes <paramref name="threshold"/> times inside a scope, a warning is logged
    /// exactly once. Outside a scope the interceptor is a single null check — zero cost.
    /// </summary>
    public static IServiceCollection AddInquiryNPlusOneDetection(this IServiceCollection services, int threshold = 2)
    {
        if (services is null) throw new ArgumentNullException(nameof(services));

        services.AddSingleton<IInquiryCommandInterceptor>(provider => new NPlusOneDetectionInterceptor(
            provider.GetRequiredService<ILogger<NPlusOneDetectionInterceptor>>(),
            threshold));
        return services;
    }
}
