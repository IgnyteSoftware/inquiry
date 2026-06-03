using Inquiry.Connections;
using Microsoft.Extensions.DependencyInjection;

namespace Inquiry.DependencyInjection;

/// <summary>
/// Shared helper used by every <c>AddInquiry&lt;Provider&gt;</c> extension to enforce the
/// single-provider DI contract: Inquiry binds <see cref="IInquiryConnectionFactory"/> globally,
/// so registering two providers on one <see cref="IServiceCollection"/> would silently overwrite
/// (last call wins) — sending every query to the wrong database with no error. The helper throws
/// up front so the misuse fails at composition time, not at runtime against the wrong DB.
/// </summary>
public static class InquiryProviderRegistration
{
    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> if a different provider's
    /// <see cref="IInquiryConnectionFactory"/> is already registered.
    /// </summary>
    /// <param name="services">The DI service collection being configured.</param>
    /// <param name="providerName">
    /// Caller-supplied display name (e.g. <c>"SQLite"</c>) for the throw message.
    /// </param>
    public static void EnsureNoExistingConnectionFactory(IServiceCollection services, string providerName)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        foreach (var descriptor in services)
        {
            if (descriptor.ServiceType == typeof(IInquiryConnectionFactory))
            {
                throw new InvalidOperationException(
                    "AddInquiry" + providerName + " was called on a service collection that already has an IInquiryConnectionFactory registered. " +
                    "Inquiry binds the connection factory globally, so a second provider registration would silently overwrite the first and send every query to the wrong database. " +
                    "If your application needs multiple databases, build a separate IServiceProvider per database (one AddInquiry<Provider> call each).");
            }
        }
    }
}
