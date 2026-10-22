using Inquiry.DependencyInjection;
using Inquiry.Northwind;
using Inquiry.Sample.Data;
using Inquiry.Sqlite.DependencyInjection;
using Microsoft.Data.Sqlite;

namespace Inquiry.Sample;

internal static class InquiryProviderSetup
{
    public static async Task ConfigureAsync(WebApplicationBuilder builder)
    {
        var provider = builder.Configuration["Inquiry:Provider"] ?? "Sqlite";
        if (!provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("This sample uses SQLite-generated stores. Set Inquiry:Provider to Sqlite. Other dialects require separately generated stores; see the sample README.");
        }

        var connectionString = Environment.GetEnvironmentVariable("INQUIRY_SAMPLE_DB");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = builder.Configuration.GetConnectionString("InquirySample.Sqlite")
                ?? throw new InvalidOperationException("Set ConnectionStrings:InquirySample.Sqlite or INQUIRY_SAMPLE_DB.");
        }

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = NorthwindSchema.SqliteDdl;
        await command.ExecuteNonQueryAsync();
        command.CommandText = WorkspaceRegistration.Schema;
        await command.ExecuteNonQueryAsync();
        builder.Services.AddInquiry().AddNorthwindSampleStores().AddInquirySqlite(connectionString);
    }
}
