using Inquiry.Northwind.Models;
using Inquiry.Northwind.Stores;
using Inquiry.Oracle.Tests.Fixtures;

namespace Inquiry.Oracle.Tests;

/// <summary>
/// End-to-end CRUD coverage for the Oracle provider, executed against the shared Northwind schema.
/// Each fact runs in its own throwaway schema so parallel tests cannot collide on table state. Skipped
/// automatically unless <see cref="OracleTestHarness.ConnectionStringEnvironmentVariable"/> is set.
/// Only the operations the Oracle provider supports in v1 are exercised — full reads plus non-returning
/// Insert/Update/Delete/Upsert (no <c>ReturnEntity = true</c>; see <see cref="OracleTestHarness"/> and
/// the E2 report for the RETURNING limitation and the SQLite-dialect fixture caveat).
/// </summary>
public sealed class NorthwindCrudIntegrationTests
{
    [OracleFact]
    public async Task StringKeyEntitySupportsFullCrud()
    {
        await using var harness = await OracleTestHarness.CreateAsync("crud_string");
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

    [OracleFact]
    public async Task UpsertInsertsThenUpdatesAcrossInvocations()
    {
        await using var harness = await OracleTestHarness.CreateAsync("upsert");
        var store = harness.GetRequiredService<CustomerStore>();
        var customer = new Customer { CustomerID = "UPS01", CompanyName = "First", Country = "USA" };

        await store.UpsertAsync(customer);
        var afterInsert = await store.SelectByKeyAsync("UPS01");

        customer.CompanyName = "Second";
        customer.Country = "Canada";
        await store.UpsertAsync(customer);
        var afterUpdate = await store.SelectByKeyAsync("UPS01");

        Assert.NotNull(afterInsert);
        Assert.Equal("First", afterInsert!.CompanyName);
        Assert.NotNull(afterUpdate);
        Assert.Equal("Second", afterUpdate!.CompanyName);
        Assert.Equal("Canada", afterUpdate.Country);
    }

    [OracleFact]
    public async Task CompositeKeyEntityRoundTripsThroughGeneratedStore()
    {
        await using var harness = await OracleTestHarness.CreateAsync("composite");
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
}
