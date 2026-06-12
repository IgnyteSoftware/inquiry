using Inquiry;
using Inquiry.AotSmoke;
using Inquiry.Commands;
using Inquiry.DependencyInjection;
using Inquiry.Sqlite.DependencyInjection;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

// NativeAOT smoke test: register Inquiry via the reflection-free generated registration, run
// generated-store CRUD against SQLite, and print the marker CI asserts on. Telemetry is enabled
// to keep the interceptor path inside the AOT compilation too.

var connectionString = new SqliteConnectionStringBuilder
{
    DataSource = "AotSmoke_" + Guid.NewGuid().ToString("N"),
    Mode = SqliteOpenMode.Memory,
    Cache = SqliteCacheMode.Shared,
}.ToString();

// Shared in-memory SQLite lives as long as one connection stays open.
await using var keeper = new SqliteConnection(connectionString);
await keeper.OpenAsync();

var services = new ServiceCollection()
    .AddInquiry()
    .AddInquiryGeneratedStores()
    .AddInquiryTelemetry()
    .AddInquirySqlite(connectionString);

await using var provider = services.BuildServiceProvider();
using var scope = provider.CreateScope();

var inquiry = scope.ServiceProvider.GetRequiredService<IInquiry>();
await inquiry.ExecuteAsync(new InquiryCommand(
    "CREATE TABLE \"TWidget\" (\"Key\" TEXT NOT NULL PRIMARY KEY, \"Name\" TEXT NOT NULL, \"IsActive\" INTEGER NOT NULL)"));

var store = scope.ServiceProvider.GetRequiredService<WidgetStore>();

var widget = new Widget { Name = "Anvil", IsActive = true };
var inserted = await store.InsertAsync(widget);
var loaded = await store.SelectByKeyAsync(widget.Key);
widget.Name = "Sprocket";
var updated = await store.UpdateAsync(widget);
var all = await store.SelectAllAsync();
var deleted = await store.DeleteByKeyAsync(widget.Key);

if (inserted != 1 || loaded?.Name != "Anvil" || !updated || all.Count != 1 || all[0].Name != "Sprocket" || !deleted)
{
    Console.Error.WriteLine("AOT-SMOKE-FAILED");
    return 1;
}

Console.WriteLine("AOT-SMOKE-OK");
return 0;
