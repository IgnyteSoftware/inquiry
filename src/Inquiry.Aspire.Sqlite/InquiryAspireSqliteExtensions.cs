using Inquiry.Aspire;
using Inquiry.Sqlite.DependencyInjection;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Registers the Inquiry SQLite provider from an Aspire resource connection name.
/// </summary>
public static class InquiryAspireSqliteExtensions
{
    /// <summary>Registers the SQLite provider, telemetry, and health check.</summary>
    public static IHostApplicationBuilder AddInquirySqlite(this IHostApplicationBuilder builder, string resourceName)
    {
        var dataSource = SqliteFactory.Instance.CreateDataSource(InquiryAspireConnectionStrings.GetRequired(builder, resourceName));
        builder.Services.AddSingleton(_ => dataSource);
        builder.Services.AddInquirySqlite(dataSource);
        builder.AddInquiryAspireDefaults();
        return builder;
    }
}
