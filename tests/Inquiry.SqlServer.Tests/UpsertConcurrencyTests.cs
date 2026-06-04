using Inquiry.Northwind.Models;
using Inquiry.Northwind.Stores;
using Inquiry.SqlServer.Tests.Fixtures;

namespace Inquiry.SqlServer.Tests;

/// <summary>
/// Pins the concurrent-upsert contract on SQL Server (audit P2 #6). The client-supplied-key path
/// uses <c>MERGE … WHEN MATCHED THEN UPDATE / WHEN NOT MATCHED THEN INSERT</c> with <c>HOLDLOCK</c>.
/// The <c>HOLDLOCK</c> hint takes a serializable range lock on the key for the duration of the
/// MERGE, so concurrent same-key MERGEs are serialized: only the first reaches WHEN NOT MATCHED
/// (insert) and the rest see the row and take WHEN MATCHED (update). No call races into a
/// duplicate-key violation, so every concurrent upsert succeeds and the surviving row's content
/// matches some input. This test asserts that contract.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class UpsertConcurrencyTests
{
    private readonly SqlServerContainerFixture _fixture;
    public UpsertConcurrencyTests(SqlServerContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task ConcurrentUpsertsOfSameKeyEndInOneRowMatchingOneInput()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateAsync(_fixture.AdminConnectionString, "upsert_concurrent");
        var store = harness.GetRequiredService<CustomerStore>();

        const int parallelism = 10;
        var inputs = Enumerable.Range(0, parallelism)
            .Select(i => new Customer { CustomerID = "CONC1", CompanyName = "Co_" + i, Country = "USA" })
            .ToArray();

        // With MERGE + HOLDLOCK, every concurrent same-key upsert succeeds (the range lock serializes
        // the WHEN NOT MATCHED inserts), so none should throw.
        await Task.WhenAll(inputs.Select(c => store.UpsertAsync(c)));

        var loaded = await store.SelectByKeyAsync("CONC1");
        Assert.NotNull(loaded);
        Assert.Contains(loaded!.CompanyName, inputs.Select(i => i.CompanyName));
    }
}
