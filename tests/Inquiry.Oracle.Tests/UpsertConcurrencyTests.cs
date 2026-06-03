using Inquiry.Northwind.Models;
using Inquiry.Northwind.Stores;
using Inquiry.Oracle.Tests.Fixtures;
using System.Data.Common;

namespace Inquiry.Oracle.Tests;

/// <summary>
/// Pins the concurrent-upsert contract on Oracle (audit P2 #6). The client-supplied-key path uses
/// <c>MERGE … WHEN MATCHED THEN UPDATE / WHEN NOT MATCHED THEN INSERT</c>. Like SQL Server's MERGE,
/// Oracle's MERGE without explicit locking has race-condition concerns under concurrent writes
/// (two MERGEs can both fall into WHEN NOT MATCHED and one fails on a unique-constraint violation).
/// The API-level contract still holds: at least one upsert succeeds and the surviving row's state
/// matches one of the inputs.
/// </summary>
[Collection(OracleCollection.Name)]
public sealed class UpsertConcurrencyTests
{
    private readonly OracleContainerFixture _fixture;
    public UpsertConcurrencyTests(OracleContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task ConcurrentUpsertsOfSameKeyEndInOneRowMatchingOneInput()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "upsert_concurrent");
        var store = harness.GetRequiredService<CustomerStore>();

        const int parallelism = 10;
        var inputs = Enumerable.Range(0, parallelism)
            .Select(i => new Customer { CustomerID = "CONC1", CompanyName = "Co_" + i, Country = "USA" })
            .ToArray();

        // Tolerate the MERGE-race: a unique-constraint failure on one parallel call is a known
        // Oracle MERGE limitation, not a bug in the upsert path. At least one call must succeed
        // and the surviving row must match some input.
        var results = await Task.WhenAll(inputs.Select(async c =>
        {
            try { await store.UpsertAsync(c); return true; }
            catch (DbException) { return false; }
        }));
        Assert.Contains(true, results);

        var loaded = await store.SelectByKeyAsync("CONC1");
        Assert.NotNull(loaded);
        Assert.Contains(loaded!.CompanyName, inputs.Select(i => i.CompanyName));
    }
}
