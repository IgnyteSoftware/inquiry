using Inquiry.MariaDb.Tests.Fixtures;
using Inquiry.Northwind.Models;
using Inquiry.Northwind.Stores;

namespace Inquiry.MariaDb.Tests;

[Collection(MariaDbCollection.Name)]
public sealed class MutationReturningIntegrationTests
{
    private readonly MariaDbContainerFixture _fixture;
    public MutationReturningIntegrationTests(MariaDbContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task InsertReturningReturnsInsertedRow()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MariaDbTestHarness.CreateAsync(_fixture.AdminConnectionString, "returning");
        var store = harness.GetRequiredService<CustomerStore>();
        var customer = new Customer { CustomerID = "RET01", CompanyName = "Returned Insert", Country = "USA" };

        var returned = await store.InsertReturningAsync(customer);

        Assert.NotNull(returned);
        Assert.Equal("RET01", returned.CustomerID);
        Assert.Equal("Returned Insert", returned.CompanyName);
        Assert.Equal("USA", returned.Country);
    }

    [SkippableFact]
    public async Task UpdateReturningReturnsUpdatedRowOrNullWhenMissing()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MariaDbTestHarness.CreateAsync(_fixture.AdminConnectionString, "returning");
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

    [SkippableFact]
    public async Task UpsertReturningReturnsInsertedOrUpdatedRow()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MariaDbTestHarness.CreateAsync(_fixture.AdminConnectionString, "returning");
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
