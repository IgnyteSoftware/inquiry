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

    // Audit P2 #6: pin the concurrent-upsert contract. Per-dialect atomicity differs (SQLite/PG/MySQL
    // are single-statement atomic for client-supplied keys; SQL Server and Oracle use MERGE; SQL Server
    // and PG use multi-statement orchestrations for database-generated keys), but the API-level
    // contract is the same: N concurrent upserts of the same key never lose the row, never deadlock
    // out, and the final row matches one of the inputs. This test asserts the contract on SQLite.
    // Parallel sibling tests in each live-provider test project exercise it against the real engines.
    [Fact]
    public async Task ConcurrentUpsertsOfSameKeyEndInOneRowMatchingOneInput()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "ConcurrentUpsert");
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
