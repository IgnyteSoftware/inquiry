using Inquiry.Aspire;
using Inquiry.MySql.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Registers the Inquiry MySQL provider from an Aspire resource connection name.
/// </summary>
public static class InquiryAspireMySqlExtensions
{
    /// <summary>Registers the MySQL provider, telemetry, and health check.</summary>
    public static IHostApplicationBuilder AddInquiryMySql(this IHostApplicationBuilder builder, string resourceName)
    {
        var connectionString = new MySqlConnectionStringBuilder(InquiryAspireConnectionStrings.GetRequired(builder, resourceName))
        {
            AllowUserVariables = true,
        }.ConnectionString;
        var dataSource = new MySqlDataSourceBuilder(connectionString).Build();
        builder.Services.AddSingleton(_ => dataSource);
        builder.Services.AddInquiryMySql(dataSource);
        builder.AddInquiryAspireDefaults();
        return builder;
    }
}
