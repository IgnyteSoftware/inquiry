using Inquiry.Connections;
using Inquiry.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Inquiry.MySql.DependencyInjection;

/// <summary>
/// Registers Inquiry MySQL/MariaDB services.
/// </summary>
public static class MySqlInquiryServiceCollectionExtensions
{
    /// <summary>
    /// Registers the MySQL/MariaDB connection factory used by generated Inquiry stores.
    /// </summary>
    public static IServiceCollection AddInquiryMySql(this IServiceCollection services, string connectionString)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        InquiryProviderRegistration.EnsureNoExistingConnectionFactory(services, "MySql");
        services.AddSingleton<IInquiryConnectionFactory>(_ => new MySqlInquiryConnectionFactory(connectionString));
        return services;
    }
}
