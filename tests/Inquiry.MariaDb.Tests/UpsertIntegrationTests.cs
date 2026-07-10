using Inquiry.MariaDb.Tests.Fixtures;
using Inquiry.Northwind.Models;
using Inquiry.Northwind.Stores;

namespace Inquiry.MariaDb.Tests;

[Collection(MariaDbCollection.Name)]
public sealed class UpsertIntegrationTests
{
    private readonly MariaDbContainerFixture _fixture;
    public UpsertIntegrationTests(MariaDbContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task UpsertInsertsWhenRowDoesNotExist()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MariaDbTestHarness.CreateAsync(_fixture.AdminConnectionString, "upsert_ins");
        var store = harness.GetRequiredService<CustomerStore>();

        var customer = new Customer { CustomerID = "NEW01", CompanyName = "New Co", Country = "USA" };
        var rows = await store.UpsertAsync(customer);

        Assert.Equal(1, rows);
        var loaded = await store.SelectByKeyAsync("NEW01");
        Assert.NotNull(loaded);
        Assert.Equal("New Co", loaded.CompanyName);
    }

    [SkippableFact]
    public async Task UpsertUpdatesExistingRow()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MariaDbTestHarness.CreateAsync(_fixture.AdminConnectionString, "upsert_upd");
        var store = harness.GetRequiredService<CustomerStore>();

        var customer = new Customer { CustomerID = "ORIG1", CompanyName = "Original", Country = "USA" };
        await store.InsertAsync(customer);

        customer.CompanyName = "Updated via Upsert";
        customer.Country = "Canada";
        await store.UpsertAsync(customer);

        var loaded = await store.SelectByKeyAsync("ORIG1");
        Assert.NotNull(loaded);
        Assert.Equal("Updated via Upsert", loaded.CompanyName);
        Assert.Equal("Canada", loaded.Country);
    }

    [SkippableFact]
    public async Task ConcurrentUpsertsOfSameKeyEndInOneRowMatchingOneInput()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MariaDbTestHarness.CreateAsync(_fixture.AdminConnectionString, "upsert_conc");
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
