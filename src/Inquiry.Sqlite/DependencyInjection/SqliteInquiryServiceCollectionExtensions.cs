using Inquiry.Connections;
using Inquiry.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Data.Common;

namespace Inquiry.Sqlite.DependencyInjection;

/// <summary>
/// Registers Inquiry SQLite services.
/// </summary>
public static class SqliteInquiryServiceCollectionExtensions
{
    /// <summary>
    /// Registers the SQLite connection factory with an externally owned data source.
    /// </summary>
    public static IServiceCollection AddInquirySqlite(this IServiceCollection services, DbDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(dataSource);

        InquiryProviderRegistration.EnsureNoExistingConnectionFactory(services, "Sqlite");
        services.AddSingleton<IInquiryConnectionFactory>(_ => new SqliteInquiryConnectionFactory(dataSource));
        return services;
    }

    /// <summary>
    /// Registers the SQLite connection factory used by generated Inquiry stores.
    /// </summary>
    public static IServiceCollection AddInquirySqlite(this IServiceCollection services, string connectionString)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        InquiryProviderRegistration.EnsureNoExistingConnectionFactory(services, "Sqlite");
        services.AddSingleton<IInquiryConnectionFactory>(_ => new SqliteInquiryConnectionFactory(connectionString));
        return services;
    }

    /// <summary>
    /// Registers the SQLite connection factory, resolving the connection string from
    /// <paramref name="configuration"/> under <c>ConnectionStrings:{connectionStringName}</c>.
    /// </summary>
    public static IServiceCollection AddInquirySqlite(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionStringName = "Inquiry")
    {
        return services.AddInquirySqlite(GetRequiredConnectionString(configuration, connectionStringName));
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
