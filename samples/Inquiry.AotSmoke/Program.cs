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
    "CREATE TABLE \"TWidget\" (\"Key\" TEXT NOT NULL PRIMARY KEY, \"Name\" TEXT NOT NULL, \"IsActive\" INTEGER NOT NULL, \"Rank\" INTEGER NOT NULL)"));

var store = scope.ServiceProvider.GetRequiredService<WidgetStore>();

var widget = new Widget { Name = "Anvil", IsActive = true, Rank = 7 };
var inserted = await store.InsertAsync(widget);
var loaded = await store.SelectByKeyAsync(widget.Key);
widget.Name = "Sprocket";
var updated = await store.UpdateAsync(widget);
var all = await store.SelectAllAsync();
// A short integer IN list binds as scalar parameters; a longer one binds one JSON array parameter.
var shortIn = await store.SelectByRanksAsync([7, 8]);
var longIn = await store.SelectByRanksAsync([7, 1, 2, 3, 4, 5, 6, 8, 9]);
var deleted = await store.DeleteByKeyAsync(widget.Key);

// JSON columns serialize through source-generated JsonSerializerContext metadata, not reflection.
await inquiry.ExecuteAsync(new InquiryCommand(
    "CREATE TABLE \"TGadget\" (\"Key\" TEXT NOT NULL PRIMARY KEY, \"Spec\" TEXT NULL, \"Tags\" TEXT NOT NULL)"));
var gadgets = scope.ServiceProvider.GetRequiredService<GadgetStore>();
var full = new Gadget
{
    Spec = new WidgetSpec { Color = "red", Sizes = [1, 2, 3] },
    Tags = new Dictionary<string, string> { ["finish"] = "matte" },
};
var empty = new Gadget { Spec = null };
var jsonInserted = await gadgets.InsertAsync(full) + await gadgets.InsertAsync(empty);
var fullLoaded = await gadgets.SelectByKeyAsync(full.Key);
var emptyLoaded = await gadgets.SelectByKeyAsync(empty.Key);
await using var specQuery = keeper.CreateCommand();
specQuery.CommandText = "SELECT COUNT(*) FROM \"TGadget\" WHERE \"Spec\" LIKE '%\"color\":\"red\"%'";
var camelCaseRows = (long)(await specQuery.ExecuteScalarAsync())!;
var jsonOk = jsonInserted == 2 && camelCaseRows == 1 &&
    fullLoaded?.Spec is { Color: "red", Sizes: [1, 2, 3] } &&
    fullLoaded.Tags.Count == 1 && fullLoaded.Tags["finish"] == "matte" &&
    emptyLoaded is { Spec: null, Tags.Count: 0 };

if (inserted != 1 || loaded?.Name != "Anvil" || !updated || all.Count != 1 || all[0].Name != "Sprocket" || shortIn.Count != 1 || longIn.Count != 1 || !deleted || !jsonOk)
{
    await Console.Error.WriteLineAsync("AOT-SMOKE-FAILED");
    return 1;
}

Console.WriteLine("AOT-SMOKE-OK");
return 0;
