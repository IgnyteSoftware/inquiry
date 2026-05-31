using Inquiry.Connections;
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

        services.AddSingleton<IInquiryConnectionFactory>(_ => new MySqlInquiryConnectionFactory(connectionString));
        return services;
    }
}
