using Inquiry.Connections;
using Inquiry.DependencyInjection;
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

        InquiryProviderRegistration.EnsureNoExistingConnectionFactory(services, "PostgreSql");
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

        InquiryProviderRegistration.EnsureNoExistingConnectionFactory(services, "PostgreSql");
        var options = new PostgreSqlInquiryOptions();
        configure(options);

        services.AddSingleton<IInquiryConnectionFactory>(_ => new PostgreSqlInquiryConnectionFactory(connectionString, options));
        return services;
    }
}
