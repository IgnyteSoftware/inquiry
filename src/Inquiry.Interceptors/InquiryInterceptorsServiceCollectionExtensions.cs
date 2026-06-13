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
    /// Logs a warning whenever a command's provider round trip takes at least
    /// <paramref name="threshold"/> (default 1 second). Command text is logged; parameter values
    /// never are.
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
}
