using Inquiry.MariaDb.Tests.Fixtures;
using Inquiry.Northwind.Models;
using Inquiry.Northwind.Stores;

namespace Inquiry.MariaDb.Tests;

/// <summary>
/// Pins the concurrent-upsert contract on MariaDB (audit P2 #6). The client-supplied-key path uses
/// single-statement <c>INSERT … ON DUPLICATE KEY UPDATE</c>, which MariaDB executes atomically — N
/// concurrent upserts of the same key produce exactly one row whose state matches one of the inputs.
/// </summary>
[Collection(MariaDbCollection.Name)]
public sealed class UpsertConcurrencyTests
{
    private readonly MariaDbContainerFixture _fixture;
    public UpsertConcurrencyTests(MariaDbContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task ConcurrentUpsertsOfSameKeyEndInOneRowMatchingOneInput()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MariaDbTestHarness.CreateAsync(_fixture.AdminConnectionString, "upsert_concurrent");
        var store = harness.GetRequiredService<CustomerStore>();

        const int parallelism = 10;
        var inputs = Enumerable.Range(0, parallelism)
            .Select(i => new Customer { CustomerID = "CONC1", CompanyName = "Co_" + i, Country = "USA" })
            .ToArray();

        await Task.WhenAll(inputs.Select(c => store.UpsertAsync(c)));

        var loaded = await store.SelectByKeyAsync("CONC1");
        Assert.NotNull(loaded);
        Assert.Contains(loaded!.CompanyName, inputs.Select(i => i.CompanyName));
    }
}
