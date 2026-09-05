using Inquiry.Northwind.Models;
using Inquiry.Northwind.Stores;
using Inquiry.Oracle.Tests.Fixtures;

namespace Inquiry.Oracle.Tests;

/// <summary>
/// Exercises returning mutations against real Oracle. Oracle has no result-set RETURNING, so each
/// op is emitted as an anonymous PL/SQL block that mutates and OPENs a ref cursor over the affected row;
/// <c>ExecuteReader</c> on the block returns that cursor, which the reader pipeline materializes unchanged.
/// A database-generated key is captured via <c>RETURNING … INTO</c> a <c>%TYPE</c> local and re-selected;
/// a client-supplied key re-selects by the supplied key.
/// </summary>
[Collection(OracleCollection.Name)]
public sealed class ReturningIntegrationTests
{
    private readonly OracleContainerFixture _fixture;
    public ReturningIntegrationTests(OracleContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task InsertReturningSurfacesGeneratedKey()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "retgen");
        var categories = harness.GetRequiredService<CategoryStore>();

        var inserted = await categories.InsertReturningAsync(new Category { CategoryName = "Beverages" });

        Assert.NotNull(inserted);
        Assert.True(inserted!.CategoryID > 0);
        Assert.Equal("Beverages", inserted.CategoryName);
        Assert.NotNull(await categories.SelectByKeyAsync(inserted.CategoryID));
    }

    [SkippableFact]
    public async Task InsertReturningSurfacesClientKeyRow()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "retcli");
        var customers = harness.GetRequiredService<CustomerStore>();

        var inserted = await customers.InsertReturningAsync(new Customer { CustomerID = "ALFKI", CompanyName = "Alfreds", Country = "Germany" });

        Assert.NotNull(inserted);
        Assert.Equal("ALFKI", inserted!.CustomerID);
        Assert.Equal("Alfreds", inserted.CompanyName);
        Assert.Equal("Germany", inserted.Country);
    }

    [SkippableFact]
    public async Task UpdateReturningSurfacesUpdatedRow()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "retupd");
        var customers = harness.GetRequiredService<CustomerStore>();

        await customers.InsertAsync(new Customer { CustomerID = "UPD01", CompanyName = "Original", Country = "USA" });
        var updated = await customers.UpdateReturningAsync(new Customer { CustomerID = "UPD01", CompanyName = "Updated", Country = "Canada" });

        Assert.NotNull(updated);
        Assert.Equal("Updated", updated!.CompanyName);
        Assert.Equal("Canada", updated.Country);
    }

    [SkippableFact]
    public async Task UpdateReturningMissingRowReturnsNull()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "retupdmiss");
        var customers = harness.GetRequiredService<CustomerStore>();

        var updated = await customers.UpdateReturningAsync(new Customer { CustomerID = "NOPE0", CompanyName = "Ghost" });

        Assert.Null(updated);
    }

    [SkippableFact]
    public async Task UpsertReturningClientKeyInsertsThenUpdates()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "retups");
        var customers = harness.GetRequiredService<CustomerStore>();

        var inserted = await customers.UpsertReturningAsync(new Customer { CustomerID = "UPS01", CompanyName = "First" });
        Assert.NotNull(inserted);
        Assert.Equal("First", inserted!.CompanyName);

        var updated = await customers.UpsertReturningAsync(new Customer { CustomerID = "UPS01", CompanyName = "Second" });
        Assert.NotNull(updated);
        Assert.Equal("Second", updated!.CompanyName);
    }
}
