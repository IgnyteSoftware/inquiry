using Inquiry.Aspire;
using Inquiry.Oracle.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Oracle.ManagedDataAccess.Client;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Registers the Inquiry Oracle provider from an Aspire resource connection name.
/// </summary>
public static class InquiryAspireOracleExtensions
{
    /// <summary>Registers the Oracle provider, telemetry, and health check.</summary>
    public static IHostApplicationBuilder AddInquiryOracle(this IHostApplicationBuilder builder, string resourceName)
    {
        var dataSource = OracleClientFactory.Instance.CreateDataSource(InquiryAspireConnectionStrings.GetRequired(builder, resourceName));
        builder.Services.AddSingleton(_ => dataSource);
        builder.Services.AddInquiryOracle(dataSource);
        builder.AddInquiryAspireDefaults();
        return builder;
    }
}
