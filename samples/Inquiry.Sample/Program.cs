using Inquiry.DependencyInjection;
using Inquiry.Sample.Data;
using Inquiry.Sample.Workflows;
using Inquiry.Sqlite.DependencyInjection;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

var databasePath = Path.Combine(AppContext.BaseDirectory, "inquiry-sample.db");
// Start each run from a clean slate so the seeded data is reproducible.
if (File.Exists(databasePath))
{
    File.Delete(databasePath);
}

var connectionString = new SqliteConnectionStringBuilder
{
    DataSource = databasePath,
}.ToString();

await SampleDatabase.CreateSchemaAsync(connectionString);

using var services = new ServiceCollection()
    .AddInquiry()
    .AddInquirySqlite(connectionString)
    .AddTransient<DirectoryWorkflow>()
    .BuildServiceProvider();

var workflow = services.GetRequiredService<DirectoryWorkflow>();
await workflow.RunAsync();
