using Inquiry.Northwind;
using Inquiry.Northwind.Models;
using Inquiry.Northwind.Stores;
using Inquiry.Sqlite.Tests.Fixtures;

namespace Inquiry.Sqlite.Tests;

public sealed class MutationReturningIntegrationTests
{
    [Fact]
    public async Task InsertReturningReturnsInsertedRow()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "Returning");
        var store = harness.GetRequiredService<CustomerStore>();
        var customer = new Customer { CustomerID = "RET01", CompanyName = "Returned Insert", Country = "USA" };

        var returned = await store.InsertReturningAsync(customer);

        Assert.NotNull(returned);
        Assert.Equal("RET01", returned.CustomerID);
        Assert.Equal("Returned Insert", returned.CompanyName);
        Assert.Equal("USA", returned.Country);
    }

    [Fact]
    public async Task UpdateReturningReturnsUpdatedRowOrNullWhenMissing()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "Returning");
        var store = harness.GetRequiredService<CustomerStore>();
        var customer = new Customer { CustomerID = "UPD01", CompanyName = "Original", Country = "USA" };
        await store.InsertAsync(customer);

        customer.CompanyName = "Returned Update";
        customer.Country = "Canada";
        var returned = await store.UpdateReturningAsync(customer);
        var missing = await store.UpdateReturningAsync(new Customer { CustomerID = "GONE1", CompanyName = "Missing", Country = "USA" });

        Assert.NotNull(returned);
        Assert.Equal("UPD01", returned.CustomerID);
        Assert.Equal("Returned Update", returned.CompanyName);
        Assert.Equal("Canada", returned.Country);
        Assert.Null(missing);
    }

    [Fact]
    public async Task UpsertReturningReturnsInsertedOrUpdatedRow()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "Returning");
        var store = harness.GetRequiredService<CustomerStore>();
        var customer = new Customer { CustomerID = "UPS01", CompanyName = "Returned Upsert Insert", Country = "USA" };

        var inserted = await store.UpsertReturningAsync(customer);
        customer.CompanyName = "Returned Upsert Update";
        customer.Country = "Canada";
        var updated = await store.UpsertReturningAsync(customer);

        Assert.NotNull(inserted);
        Assert.Equal("Returned Upsert Insert", inserted.CompanyName);
        Assert.NotNull(updated);
        Assert.Equal("UPS01", updated.CustomerID);
        Assert.Equal("Returned Upsert Update", updated.CompanyName);
        Assert.Equal("Canada", updated.Country);
    }
}
