using Inquiry.Northwind;
using Inquiry.Northwind.Models;
using Inquiry.Northwind.Stores;
using Inquiry.Sqlite.Tests.Fixtures;

namespace Inquiry.Sqlite.Tests;

public sealed class UpsertIntegrationTests
{
    [Fact]
    public async Task UpsertInsertsWhenRowDoesNotExist()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "Upsert");
        var store = harness.GetRequiredService<CustomerStore>();

        var customer = new Customer { CustomerID = "NEW01", CompanyName = "New Co", Country = "USA" };
        var rows = await store.UpsertAsync(customer);

        Assert.Equal(1, rows);
        var loaded = await store.SelectByKeyAsync("NEW01");
        Assert.NotNull(loaded);
        Assert.Equal("New Co", loaded.CompanyName);
    }

    [Fact]
    public async Task UpsertUpdatesExistingRow()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "Upsert");
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
}
