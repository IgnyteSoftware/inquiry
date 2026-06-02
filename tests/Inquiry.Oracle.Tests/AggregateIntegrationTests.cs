using Inquiry.Northwind.Models;
using Inquiry.Northwind.Stores;
using Inquiry.Oracle.Tests.Fixtures;

namespace Inquiry.Oracle.Tests;

/// <summary>
/// W5 aggregations over the Northwind <c>Product</c> entity against real Oracle: COUNT, MAX, and SUM
/// over the scalar pipeline, plus a NULL SUM over an empty table.
/// </summary>
[Collection(OracleCollection.Name)]
public sealed class AggregateIntegrationTests
{
    private readonly OracleContainerFixture _fixture;
    public AggregateIntegrationTests(OracleContainerFixture fixture) => _fixture = fixture;

    private static async Task SeedAsync(OracleTestHarness harness)
    {
        var products = harness.GetRequiredService<ProductStore>();
        await products.InsertAsync(new Product { ProductName = "A", UnitPrice = 10m, CategoryID = null, Discontinued = false });
        await products.InsertAsync(new Product { ProductName = "B", UnitPrice = 20m, CategoryID = null, Discontinued = false });
        await products.InsertAsync(new Product { ProductName = "C", UnitPrice = 30m, CategoryID = null, Discontinued = false });
    }

    [SkippableFact]
    public async Task ComputesCountMaxSum()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "agg");
        await SeedAsync(harness);
        var products = harness.GetRequiredService<ProductStore>();

        Assert.Equal(3L, await products.CountAsync());
        Assert.Equal(30m, await products.MaxUnitPriceAsync());
        Assert.Equal(60m, await products.SumUnitPriceAsync());
    }

    [SkippableFact]
    public async Task NullAggregateOverNoRows()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "aggempty");
        var products = harness.GetRequiredService<ProductStore>();

        Assert.Equal(0L, await products.CountAsync());
        Assert.Null(await products.SumUnitPriceAsync());
    }
}
