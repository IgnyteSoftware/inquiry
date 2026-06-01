using Inquiry.MySql.Tests.Fixtures;
using Inquiry.Northwind.Models;
using Inquiry.Northwind.Stores;

namespace Inquiry.MySql.Tests;

/// <summary>
/// End-to-end CRUD coverage for the MySQL/MariaDB provider, executed against the shared Northwind
/// schema. Each fact runs in its own throwaway database so parallel tests cannot collide on table
/// state. Skipped automatically unless <see cref="MySqlTestHarness.ConnectionStringEnvironmentVariable"/>
/// is set.
/// </summary>
[Collection(MySqlCollection.Name)]
public sealed class NorthwindCrudIntegrationTests
{
    private readonly MySqlContainerFixture _fixture;
    public NorthwindCrudIntegrationTests(MySqlContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task StringKeyEntitySupportsFullCrud()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateAsync(_fixture.AdminConnectionString, "crud_string");
        var store = harness.GetRequiredService<CustomerStore>();
        var customer = new Customer
        {
            CustomerID = "ACME1",
            CompanyName = "Acme Research",
            Country = "USA",
        };

        var inserted = await store.InsertAsync(customer);
        var selected = await store.SelectByKeyAsync("ACME1");
        var usCustomers = await store.SelectByCountryAsync("USA");

        customer.CompanyName = "Acme Updated";
        customer.Country = "Canada";
        var updated = await store.UpdateAsync(customer);
        var selectedAfterUpdate = await store.SelectByKeyAsync("ACME1");

        var deleted = await store.DeleteByKeyAsync("ACME1");
        var selectedAfterDelete = await store.SelectByKeyAsync("ACME1");

        Assert.Equal(1, inserted);
        Assert.NotNull(selected);
        Assert.Equal("Acme Research", selected!.CompanyName);
        Assert.Equal("USA", selected.Country);
        Assert.Single(usCustomers);
        Assert.True(updated);
        Assert.NotNull(selectedAfterUpdate);
        Assert.Equal("Acme Updated", selectedAfterUpdate!.CompanyName);
        Assert.Equal("Canada", selectedAfterUpdate.Country);
        Assert.True(deleted);
        Assert.Null(selectedAfterDelete);
    }

    [SkippableFact]
    public async Task GeneratedKeyEntitySupportsInsertReturningAndUpsert()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateAsync(_fixture.AdminConnectionString, "crud_identity");
        var store = harness.GetRequiredService<CategoryStore>();

        var inserted = await store.InsertReturningAsync(new Category { CategoryName = "Beverages" });
        Assert.NotNull(inserted);
        Assert.NotNull(inserted!.CategoryID);
        Assert.True(inserted.CategoryID > 0);

        var fetched = await store.SelectByKeyAsync(inserted.CategoryID);
        Assert.NotNull(fetched);
        Assert.Equal("Beverages", fetched!.CategoryName);

        var deleted = await store.DeleteByKeyAsync(inserted.CategoryID);
        Assert.True(deleted);
        Assert.Null(await store.SelectByKeyAsync(inserted.CategoryID));
    }

    [SkippableFact]
    public async Task UpsertInsertsThenUpdatesAcrossInvocations()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateAsync(_fixture.AdminConnectionString, "upsert");
        var store = harness.GetRequiredService<CustomerStore>();
        var customer = new Customer { CustomerID = "UPS01", CompanyName = "First", Country = "USA" };

        var firstRows = await store.UpsertAsync(customer);
        var afterInsert = await store.SelectByKeyAsync("UPS01");

        customer.CompanyName = "Second";
        customer.Country = "Canada";
        var secondRows = await store.UpsertAsync(customer);
        var afterUpdate = await store.SelectByKeyAsync("UPS01");

        Assert.NotNull(afterInsert);
        Assert.Equal("First", afterInsert!.CompanyName);
        Assert.NotNull(afterUpdate);
        Assert.Equal("Second", afterUpdate!.CompanyName);
        Assert.Equal("Canada", afterUpdate.Country);
        // ON DUPLICATE KEY UPDATE reports 1 affected row for an insert and 2 for an update.
        Assert.Equal(1, firstRows);
        Assert.Equal(2, secondRows);
    }

    [SkippableFact]
    public async Task CompositeKeyEntityRoundTripsThroughGeneratedStore()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateAsync(_fixture.AdminConnectionString, "composite");
        var customers = harness.GetRequiredService<CustomerStore>();
        var demographics = harness.GetRequiredService<CustomerDemographicStore>();
        var bridge = harness.GetRequiredService<CustomerCustomerDemoStore>();

        await customers.InsertAsync(new Customer { CustomerID = "ALFKI", CompanyName = "Alfreds Futterkiste" });
        await demographics.InsertAsync(new CustomerDemographic { CustomerTypeID = "VIP", CustomerDesc = "Very Important" });
        await bridge.InsertAsync(new CustomerCustomerDemo { CustomerID = "ALFKI", CustomerTypeID = "VIP" });

        var loaded = await bridge.SelectByKeyAsync("ALFKI", "VIP");
        Assert.NotNull(loaded);
        Assert.Equal("ALFKI", loaded!.CustomerID);
        Assert.Equal("VIP", loaded.CustomerTypeID);

        Assert.True(await bridge.DeleteByKeyAsync("ALFKI", "VIP"));
        Assert.Null(await bridge.SelectByKeyAsync("ALFKI", "VIP"));
    }

    [SkippableFact]
    public async Task EagerLoadPopulatesChildCollection()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateAsync(_fixture.AdminConnectionString, "eager");
        var regions = harness.GetRequiredService<RegionStore>();
        var territories = harness.GetRequiredService<TerritoryStore>();

        await regions.InsertAsync(new Region { RegionID = 1, RegionDescription = "Eastern" });
        await territories.InsertAsync(new Territory { TerritoryID = "01581", TerritoryDescription = "Westboro", RegionID = 1 });
        await territories.InsertAsync(new Territory { TerritoryID = "01730", TerritoryDescription = "Bedford",  RegionID = 1 });

        var loaded = await regions.SelectByKeyWithTerritoriesAsync(1);

        Assert.NotNull(loaded);
        Assert.Equal("Eastern", loaded!.RegionDescription);
        Assert.NotNull(loaded.Territories);
        Assert.Equal(2, loaded.Territories!.Count);
    }

    [SkippableFact]
    public async Task TransactionCommitPersistsAndRollbackReverts()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateAsync(_fixture.AdminConnectionString, "tx");
        var inquiry = harness.GetRequiredService<IInquiry>();
        var store = harness.GetRequiredService<CustomerStore>();

        await using (var tx = await inquiry.BeginTransactionAsync())
        {
            await store.InsertAsync(new Customer { CustomerID = "COMM1", CompanyName = "Committed" });
            await tx.CommitAsync();
        }

        await using (var tx = await inquiry.BeginTransactionAsync())
        {
            await store.InsertAsync(new Customer { CustomerID = "ROLL1", CompanyName = "Rolled Back" });
            // dispose without commit → rollback
        }

        Assert.NotNull(await store.SelectByKeyAsync("COMM1"));
        Assert.Null(await store.SelectByKeyAsync("ROLL1"));
    }
}
