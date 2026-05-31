using Inquiry.Connections;
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

        var options = new SqlServerInquiryOptions();
        configure(options);

        services.AddSingleton<IInquiryConnectionFactory>(_ => new SqlServerInquiryConnectionFactory(connectionString, options));
        return services;
    }
}
