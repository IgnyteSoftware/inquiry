using Inquiry.BulkCopy;
using Inquiry.Connections;
using Inquiry.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Inquiry.PostgreSql.DependencyInjection;

/// <summary>
/// Registers Inquiry PostgreSQL services.
/// </summary>
public static class PostgreSqlInquiryServiceCollectionExtensions
{
    /// <summary>
    /// Registers the PostgreSQL connection factory with an externally owned data source.
    /// </summary>
    public static IServiceCollection AddInquiryPostgreSql(
        this IServiceCollection services,
        NpgsqlDataSource dataSource)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (dataSource is null)
        {
            throw new ArgumentNullException(nameof(dataSource));
        }

        InquiryProviderRegistration.EnsureNoExistingConnectionFactory(services, "PostgreSql");
        services.AddSingleton<IInquiryConnectionFactory>(_ => new PostgreSqlInquiryConnectionFactory(dataSource));
        services.AddSingleton<IInquiryBulkCopier, PostgreSqlBulkCopier>();
        return services;
    }

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
        services.AddSingleton<IInquiryBulkCopier, PostgreSqlBulkCopier>();
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
        services.AddSingleton<IInquiryBulkCopier, PostgreSqlBulkCopier>();
        return services;
    }

    /// <summary>
    /// Registers the PostgreSQL connection factory, resolving the connection string from
    /// <paramref name="configuration"/> under <c>ConnectionStrings:{connectionStringName}</c>.
    /// </summary>
    public static IServiceCollection AddInquiryPostgreSql(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionStringName = "Inquiry")
    {
        return services.AddInquiryPostgreSql(GetRequiredConnectionString(configuration, connectionStringName));
    }

    /// <summary>
    /// Registers the PostgreSQL connection factory with provider-specific options, resolving the
    /// connection string from <paramref name="configuration"/> under
    /// <c>ConnectionStrings:{connectionStringName}</c>.
    /// </summary>
    public static IServiceCollection AddInquiryPostgreSql(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<PostgreSqlInquiryOptions> configure,
        string connectionStringName = "Inquiry")
    {
        return services.AddInquiryPostgreSql(GetRequiredConnectionString(configuration, connectionStringName), configure);
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
