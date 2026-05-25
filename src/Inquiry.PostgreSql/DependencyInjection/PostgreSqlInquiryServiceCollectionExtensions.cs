using Inquiry.Connections;
using Inquiry.Sql;
using Microsoft.Extensions.DependencyInjection;

namespace Inquiry.PostgreSql.DependencyInjection;

/// <summary>
/// Registers Inquiry PostgreSQL services.
/// </summary>
public static class PostgreSqlInquiryServiceCollectionExtensions
{
    /// <summary>
    /// Registers the PostgreSQL connection factory used by generated Inquiry stores.
    /// </summary>
    public static IServiceCollection AddInquiryPostgreSql(this IServiceCollection services, string connectionString)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        services.AddSingleton<IInquiryConnectionFactory>(_ => new PostgreSqlInquiryConnectionFactory(connectionString));
        services.AddSingleton<InquirySqlDialect, PostgreSqlInquirySqlDialect>();
        return services;
    }
}
