using Inquiry.Connections;
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

        services.AddSingleton<IInquiryConnectionFactory>(_ => new OracleInquiryConnectionFactory(connectionString));
        return services;
    }
}
