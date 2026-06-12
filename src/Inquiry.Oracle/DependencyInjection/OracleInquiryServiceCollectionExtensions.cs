using Inquiry.Connections;
using Inquiry.DependencyInjection;
using Microsoft.Extensions.Configuration;
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

        InquiryProviderRegistration.EnsureNoExistingConnectionFactory(services, "Oracle");
        services.AddSingleton<IInquiryConnectionFactory>(_ => new OracleInquiryConnectionFactory(connectionString));
        return services;
    }

    /// <summary>
    /// Registers the Oracle connection factory with provider-specific options (failover).
    /// </summary>
    public static IServiceCollection AddInquiryOracle(
        this IServiceCollection services,
        string connectionString,
        Action<OracleInquiryOptions> configure)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (configure is null)
        {
            throw new ArgumentNullException(nameof(configure));
        }

        InquiryProviderRegistration.EnsureNoExistingConnectionFactory(services, "Oracle");
        var options = new OracleInquiryOptions();
        configure(options);

        services.AddSingleton<IInquiryConnectionFactory>(_ => new OracleInquiryConnectionFactory(connectionString, options));
        return services;
    }

    /// <summary>
    /// Registers the Oracle connection factory, resolving the connection string from
    /// <paramref name="configuration"/> under <c>ConnectionStrings:{connectionStringName}</c>.
    /// </summary>
    public static IServiceCollection AddInquiryOracle(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionStringName = "Inquiry")
    {
        return services.AddInquiryOracle(GetRequiredConnectionString(configuration, connectionStringName));
    }

    /// <summary>
    /// Registers the Oracle connection factory with provider-specific options, resolving the
    /// connection string from <paramref name="configuration"/> under
    /// <c>ConnectionStrings:{connectionStringName}</c>.
    /// </summary>
    public static IServiceCollection AddInquiryOracle(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<OracleInquiryOptions> configure,
        string connectionStringName = "Inquiry")
    {
        return services.AddInquiryOracle(GetRequiredConnectionString(configuration, connectionStringName), configure);
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
