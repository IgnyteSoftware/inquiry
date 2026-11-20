using Inquiry.DependencyInjection;
using Inquiry.Journeys;
using Inquiry.Sqlite.DependencyInjection;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

// Walks each application journey against SQLite with generated stores. A failed check throws,
// so the process exits nonzero and never prints the final marker.

var connectionString = new SqliteConnectionStringBuilder
{
    DataSource = "inquiry-journeys",
    Mode = SqliteOpenMode.Memory,
    Cache = SqliteCacheMode.Shared,
    ForeignKeys = true,
}.ToString();

// A shared in-memory database lives only while one connection stays open.
await using var keeper = new SqliteConnection(connectionString);
await keeper.OpenAsync();

var services = new ServiceCollection()
    .AddInquiry(options => options.ThrowOnConcurrencyConflict = true)
    .AddInquiryGeneratedStores()
    .AddInquirySqlite(connectionString);

await using var provider = services.BuildServiceProvider(
    new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
await using var scope = provider.CreateAsyncScope();
var journeys = new JourneyRunner(scope.ServiceProvider);

Console.WriteLine(await journeys.NewSchemaAsync());
Console.WriteLine(await journeys.ExistingSchemaAsync());
Console.WriteLine(await journeys.OrderAndLinesAsync());
Console.WriteLine(await journeys.RoleLinksAsync());
Console.WriteLine(await journeys.ConcurrentEditsAsync());
Console.WriteLine(await journeys.TenantIsolationAsync());
Console.WriteLine(await journeys.SelectiveEagerAsync());
Console.WriteLine(await journeys.HandwrittenSqlAsync());
Console.WriteLine(await journeys.AotJsonAsync());
Console.WriteLine("JOURNEYS-OK");
