using Inquiry.Aspire;
using Inquiry.PostgreSql.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Registers the Inquiry PostgreSQL provider from an Aspire resource connection name.
/// </summary>
public static class InquiryAspirePostgreSqlExtensions
{
    /// <summary>Registers the PostgreSQL provider, telemetry, and health check.</summary>
    public static IHostApplicationBuilder AddInquiryPostgreSql(this IHostApplicationBuilder builder, string resourceName)
    {
        var dataSource = new NpgsqlDataSourceBuilder(InquiryAspireConnectionStrings.GetRequired(builder, resourceName)).Build();
        builder.Services.AddSingleton(_ => dataSource);
        builder.Services.AddInquiryPostgreSql(dataSource);
        builder.AddInquiryAspireDefaults();
        return builder;
    }
}
