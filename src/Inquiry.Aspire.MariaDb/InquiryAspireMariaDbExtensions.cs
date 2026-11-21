using Inquiry.Aspire;
using Inquiry.MariaDb.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Registers the Inquiry MariaDB provider from an Aspire resource connection name.
/// </summary>
public static class InquiryAspireMariaDbExtensions
{
    /// <summary>Registers the MariaDB provider, telemetry, and health check.</summary>
    public static IHostApplicationBuilder AddInquiryMariaDb(this IHostApplicationBuilder builder, string resourceName)
    {
        var dataSource = new MySqlDataSourceBuilder(InquiryAspireConnectionStrings.GetRequired(builder, resourceName)).Build();
        builder.Services.AddSingleton(_ => dataSource);
        builder.Services.AddInquiryMariaDb(dataSource);
        builder.AddInquiryAspireDefaults();
        return builder;
    }
}
