using Inquiry.Northwind.Models;
using Inquiry.Northwind.Stores;
using Inquiry.PostgreSql.Tests.Fixtures;

namespace Inquiry.PostgreSql.Tests;

/// <summary>
/// Pins the concurrent-upsert contract on PostgreSQL (audit P2 #6). The client-supplied-key
/// path uses single-statement <c>INSERT … ON CONFLICT (…) DO UPDATE</c>, which PostgreSQL
/// executes atomically — N concurrent upserts of the same key produce exactly one row whose
/// state matches one of the inputs.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
public sealed class UpsertConcurrencyTests
{
    private readonly PostgreSqlContainerFixture _fixture;
    public UpsertConcurrencyTests(PostgreSqlContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task ConcurrentUpsertsOfSameKeyEndInOneRowMatchingOneInput()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await PostgreSqlTestHarness.CreateAsync(_fixture.AdminConnectionString, "upsert_concurrent");
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
