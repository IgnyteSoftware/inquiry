using Inquiry;
using Inquiry.DependencyInjection;
using Inquiry.Sample.Data;
using Inquiry.Sqlite;
using Inquiry.Sqlite.DependencyInjection;
using Inquiry.Sample.Workflows;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

var databasePath = Path.Combine(AppContext.BaseDirectory, "inquiry-sample.db");
var connectionString = new SqliteConnectionStringBuilder
{
    DataSource = databasePath,
}.ToString();

await SampleDatabase.CreateSchemaAsync(connectionString);

using var services = new ServiceCollection()
    .AddInquiry()
    .AddInquirySqlite(connectionString)
    .AddTransient<OrganizationWorkflow>()
    .BuildServiceProvider();

var workflow = services.GetRequiredService<OrganizationWorkflow>();
await workflow.RunAsync();
