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
}
