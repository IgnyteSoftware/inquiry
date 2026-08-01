using Inquiry.BulkCopy;
using Inquiry.Connections;
using Inquiry.DependencyInjection;
using Microsoft.Extensions.Configuration;
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

        InquiryProviderRegistration.EnsureNoExistingConnectionFactory(services, "SqlServer");
        services.AddSingleton<IInquiryConnectionFactory>(_ => new SqlServerInquiryConnectionFactory(connectionString));
        services.AddSingleton<IInquiryBulkCopier, SqlServerBulkCopier>();
        return services;
    }

    /// <summary>
    /// Registers the SQL Server connection factory with provider-specific options (cloud
    /// compatibility, transient-fault retry, access-token auth).
    /// </summary>
    public static IServiceCollection AddInquirySqlServer(
        this IServiceCollection services,
        string connectionString,
        Action<SqlServerInquiryOptions> configure)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (configure is null)
        {
            throw new ArgumentNullException(nameof(configure));
        }

        InquiryProviderRegistration.EnsureNoExistingConnectionFactory(services, "SqlServer");
        var options = new SqlServerInquiryOptions();
        configure(options);

        services.AddSingleton<IInquiryConnectionFactory>(_ => new SqlServerInquiryConnectionFactory(connectionString, options));
        services.AddSingleton<IInquiryBulkCopier, SqlServerBulkCopier>();
        return services;
    }

    /// <summary>
    /// Registers the SQL Server connection factory, resolving the connection string from
    /// <paramref name="configuration"/> under <c>ConnectionStrings:{connectionStringName}</c>.
    /// </summary>
    public static IServiceCollection AddInquirySqlServer(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionStringName = "Inquiry")
    {
        return services.AddInquirySqlServer(GetRequiredConnectionString(configuration, connectionStringName));
    }

    /// <summary>
    /// Registers the SQL Server connection factory with provider-specific options, resolving the
    /// connection string from <paramref name="configuration"/> under
    /// <c>ConnectionStrings:{connectionStringName}</c>.
    /// </summary>
    public static IServiceCollection AddInquirySqlServer(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<SqlServerInquiryOptions> configure,
        string connectionStringName = "Inquiry")
    {
        return services.AddInquirySqlServer(GetRequiredConnectionString(configuration, connectionStringName), configure);
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
