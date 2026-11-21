using Inquiry.Aspire;
using Inquiry.SqlServer.DependencyInjection;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Registers the Inquiry SQL Server provider from an Aspire resource connection name.
/// </summary>
public static class InquiryAspireSqlServerExtensions
{
    /// <summary>Registers the SQL Server provider, telemetry, and health check.</summary>
    public static IHostApplicationBuilder AddInquirySqlServer(this IHostApplicationBuilder builder, string resourceName)
    {
        var dataSource = SqlClientFactory.Instance.CreateDataSource(InquiryAspireConnectionStrings.GetRequired(builder, resourceName));
        builder.Services.AddSingleton(_ => dataSource);
        builder.Services.AddInquirySqlServer(dataSource);
        builder.AddInquiryAspireDefaults();
        return builder;
    }
}
