using Inquiry.Connections;
using Inquiry.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Inquiry.Sqlite.DependencyInjection;

/// <summary>
/// Registers Inquiry SQLite services.
/// </summary>
public static class SqliteInquiryServiceCollectionExtensions
{
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
}
