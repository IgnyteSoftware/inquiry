using Inquiry.BulkCopy;
using Inquiry.Connections;
using Inquiry.DependencyInjection;
using Microsoft.Extensions.Configuration;
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
        services.AddSingleton<IInquiryBulkCopier, MySqlBulkCopier>();
        return services;
    }

    /// <summary>
    /// Registers the MySQL/MariaDB connection factory with provider-specific options (failover).
    /// </summary>
    public static IServiceCollection AddInquiryMySql(
        this IServiceCollection services,
        string connectionString,
        Action<MySqlInquiryOptions> configure)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (configure is null)
        {
            throw new ArgumentNullException(nameof(configure));
        }

        InquiryProviderRegistration.EnsureNoExistingConnectionFactory(services, "MySql");
        var options = new MySqlInquiryOptions();
        configure(options);

        services.AddSingleton<IInquiryConnectionFactory>(_ => new MySqlInquiryConnectionFactory(connectionString, options));
        services.AddSingleton<IInquiryBulkCopier, MySqlBulkCopier>();
        return services;
    }

    /// <summary>
    /// Registers the MySQL/MariaDB connection factory, resolving the connection string from
    /// <paramref name="configuration"/> under <c>ConnectionStrings:{connectionStringName}</c>.
    /// </summary>
    public static IServiceCollection AddInquiryMySql(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionStringName = "Inquiry")
    {
        return services.AddInquiryMySql(GetRequiredConnectionString(configuration, connectionStringName));
    }

    /// <summary>
    /// Registers the MySQL/MariaDB connection factory with provider-specific options, resolving the
    /// connection string from <paramref name="configuration"/> under
    /// <c>ConnectionStrings:{connectionStringName}</c>.
    /// </summary>
    public static IServiceCollection AddInquiryMySql(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<MySqlInquiryOptions> configure,
        string connectionStringName = "Inquiry")
    {
        return services.AddInquiryMySql(GetRequiredConnectionString(configuration, connectionStringName), configure);
    }

    private static string GetRequiredConnectionString(IConfiguration configuration, string connectionStringName)
    {
        if (configuration is null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        var connectionString = configuration.GetConnectionString(connectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Connection string '{connectionStringName}' was not found in configuration. " +
                $"Add a 'ConnectionStrings:{connectionStringName}' entry, or pass the name of a configured connection string.");
        }

        return connectionString;
    }
}
