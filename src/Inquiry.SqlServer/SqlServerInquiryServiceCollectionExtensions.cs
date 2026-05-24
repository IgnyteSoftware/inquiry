using Inquiry;
using Inquiry.SqlServer;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers Inquiry SQL Server services.
/// </summary>
public static class SqlServerInquiryServiceCollectionExtensions
{
    /// <summary>
    /// Registers the SQL Server connection factory used by generated Inquiry stores.
    /// </summary>
    public static IServiceCollection AddInquirySqlServer(this IServiceCollection services, string connectionString)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        services.AddInquiryCore();
        services.AddSingleton<IInquiryConnectionFactory>(_ => new SqlServerInquiryConnectionFactory(connectionString));
        return services;
    }
}
