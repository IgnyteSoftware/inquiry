using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Inquiry.Aspire;

// The Aspire provider packages use this through InternalsVisibleTo. Every Inquiry package pins its
// siblings to its own exact version, so the internal contract cannot drift between them.
internal static class InquiryAspireConnectionStrings
{
    public static string GetRequired(IHostApplicationBuilder builder, string resourceName)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);

        var connectionString = builder.Configuration.GetConnectionString(resourceName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Connection string '{resourceName}' was not found in configuration. " +
                $"Add a 'ConnectionStrings:{resourceName}' entry, or pass the name of a configured connection string.");
        }

        return connectionString;
    }
}
