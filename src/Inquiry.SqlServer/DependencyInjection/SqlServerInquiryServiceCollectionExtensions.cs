using Inquiry.Connections;
using Inquiry.Sql;
using Inquiry.SqlServer;
using Microsoft.Extensions.DependencyInjection;

namespace Inquiry.SqlServer.DependencyInjection;

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

        services.AddSingleton<IInquiryConnectionFactory>(_ => new SqlServerInquiryConnectionFactory(connectionString));
        services.AddSingleton<InquirySqlDialect, SqlServerInquirySqlDialect>();
        return services;
    }
}
