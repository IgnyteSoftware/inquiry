using Inquiry.Connections;
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
        return services;
    }

    /// <summary>
    /// Registers the PostgreSQL connection factory with provider-specific options (cloud
    /// compatibility and transient-fault retry).
    /// </summary>
    public static IServiceCollection AddInquiryPostgreSql(
        this IServiceCollection services,
        string connectionString,
        Action<PostgreSqlInquiryOptions> configure)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (configure is null)
        {
            throw new ArgumentNullException(nameof(configure));
        }

        var options = new PostgreSqlInquiryOptions();
        configure(options);

        services.AddSingleton<IInquiryConnectionFactory>(_ => new PostgreSqlInquiryConnectionFactory(connectionString, options));
        return services;
    }
}
