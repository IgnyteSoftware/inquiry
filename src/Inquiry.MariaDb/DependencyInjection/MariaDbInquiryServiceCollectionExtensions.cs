using Inquiry.BulkCopy;
using Inquiry.Connections;
using Inquiry.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Inquiry.MariaDb.DependencyInjection;

/// <summary>
/// Registers Inquiry MariaDB services.
/// </summary>
public static class MariaDbInquiryServiceCollectionExtensions
{
    /// <summary>
    /// Registers the MariaDB connection factory used by generated Inquiry stores.
    /// </summary>
    public static IServiceCollection AddInquiryMariaDb(this IServiceCollection services, string connectionString)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        InquiryProviderRegistration.EnsureNoExistingConnectionFactory(services, "MariaDb");
        services.AddSingleton<IInquiryConnectionFactory>(_ => new MariaDbInquiryConnectionFactory(connectionString));
        services.AddSingleton<IInquiryBulkCopier, MariaDbBulkCopier>();
        return services;
    }

    /// <summary>
    /// Registers the MariaDB connection factory with provider-specific options (cloud
    /// compatibility, transient-fault retry, and failover).
    /// </summary>
    public static IServiceCollection AddInquiryMariaDb(
        this IServiceCollection services,
        string connectionString,
        Action<MariaDbInquiryOptions> configure)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (configure is null)
        {
            throw new ArgumentNullException(nameof(configure));
        }

        InquiryProviderRegistration.EnsureNoExistingConnectionFactory(services, "MariaDb");
        var options = new MariaDbInquiryOptions();
        configure(options);

        services.AddSingleton<IInquiryConnectionFactory>(_ => new MariaDbInquiryConnectionFactory(connectionString, options));
        services.AddSingleton<IInquiryBulkCopier, MariaDbBulkCopier>();
        return services;
    }

    /// <summary>
    /// Registers the MariaDB connection factory, resolving the connection string from
    /// <paramref name="configuration"/> under <c>ConnectionStrings:{connectionStringName}</c>.
    /// </summary>
    public static IServiceCollection AddInquiryMariaDb(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionStringName = "Inquiry")
    {
        return services.AddInquiryMariaDb(GetRequiredConnectionString(configuration, connectionStringName));
    }

    /// <summary>
    /// Registers the MariaDB connection factory with provider-specific options, resolving the
    /// connection string from <paramref name="configuration"/> under
    /// <c>ConnectionStrings:{connectionStringName}</c>.
    /// </summary>
    public static IServiceCollection AddInquiryMariaDb(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<MariaDbInquiryOptions> configure,
        string connectionStringName = "Inquiry")
    {
        return services.AddInquiryMariaDb(GetRequiredConnectionString(configuration, connectionStringName), configure);
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
