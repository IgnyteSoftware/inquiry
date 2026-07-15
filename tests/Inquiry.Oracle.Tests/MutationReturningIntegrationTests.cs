using Inquiry.Northwind.Models;
using Inquiry.Northwind.Stores;
using Inquiry.Oracle.Tests.Fixtures;

namespace Inquiry.Oracle.Tests;

[Collection(OracleCollection.Name)]
public sealed class MutationReturningIntegrationTests
{
    private readonly OracleContainerFixture _fixture;
    public MutationReturningIntegrationTests(OracleContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task InsertReturningReturnsInsertedRow()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "returning");
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
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "returning");
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

    // Oracle supports UpsertReturning for client-supplied keys (BEGIN MERGE...; OPEN :rc FOR
    // SELECT...; END;) but not for generated keys (INQ039 — MERGE joins on the key, which is
    // NULL for a generated key, so it can never match).

    [SkippableFact]
    public async Task UpsertReturningReturnsUpsertedRow()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "returning");
        var store = harness.GetRequiredService<CustomerStore>();

        // Insert branch — row does not exist yet.
        var inserted = await store.UpsertReturningAsync(new Customer { CustomerID = "UPS01", CompanyName = "New", Country = "USA" });
        Assert.NotNull(inserted);
        Assert.Equal("UPS01", inserted!.CustomerID);
        Assert.Equal("New", inserted.CompanyName);

        // Update branch — row already exists.
        var updated = await store.UpsertReturningAsync(new Customer { CustomerID = "UPS01", CompanyName = "Updated", Country = "Canada" });
        Assert.NotNull(updated);
        Assert.Equal("Updated", updated!.CompanyName);
        Assert.Equal("Canada", updated.Country);
    }
}
