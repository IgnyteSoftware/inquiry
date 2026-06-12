using Inquiry.Connections;
using Inquiry.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Inquiry.Oracle.DependencyInjection;

/// <summary>
/// Registers Inquiry Oracle services.
/// </summary>
public static class OracleInquiryServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Oracle connection factory used by generated Inquiry stores.
    /// </summary>
    public static IServiceCollection AddInquiryOracle(this IServiceCollection services, string connectionString)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        InquiryProviderRegistration.EnsureNoExistingConnectionFactory(services, "Oracle");
        services.AddSingleton<IInquiryConnectionFactory>(_ => new OracleInquiryConnectionFactory(connectionString));
        return services;
    }

    /// <summary>
    /// Registers the Oracle connection factory with provider-specific options (failover).
    /// </summary>
    public static IServiceCollection AddInquiryOracle(
        this IServiceCollection services,
        string connectionString,
        Action<OracleInquiryOptions> configure)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (configure is null)
        {
            throw new ArgumentNullException(nameof(configure));
        }

        InquiryProviderRegistration.EnsureNoExistingConnectionFactory(services, "Oracle");
        var options = new OracleInquiryOptions();
        configure(options);

        services.AddSingleton<IInquiryConnectionFactory>(_ => new OracleInquiryConnectionFactory(connectionString, options));
        return services;
    }
}
