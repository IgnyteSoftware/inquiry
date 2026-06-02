using System.Linq;
using Inquiry.Northwind.Models;
using Inquiry.Northwind.Stores;
using Inquiry.Oracle.Tests.Fixtures;

namespace Inquiry.Oracle.Tests;

/// <summary>
/// W5b projection over the Northwind <c>Product</c> entity against real Oracle: a projection-returning
/// SelectAll materializes only the declared columns (ProductID, ProductName, UnitPrice) into
/// <see cref="ProductSummary"/>.
/// </summary>
[Collection(OracleCollection.Name)]
public sealed class ProjectionIntegrationTests
{
    private readonly OracleContainerFixture _fixture;
    public ProjectionIntegrationTests(OracleContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task ProjectionMaterializesDeclaredColumnsSubset()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "proj");
        var products = harness.GetRequiredService<ProductStore>();
        await products.InsertAsync(new Product { ProductName = "Chai",  UnitPrice = 18m, CategoryID = null, Discontinued = false });
        await products.InsertAsync(new Product { ProductName = "Chang", UnitPrice = 19m, CategoryID = null, Discontinued = false });

        var summaries = await products.SummariesAsync();

        Assert.Equal(2, summaries.Count);
        Assert.Contains(summaries, s => s.ProductName == "Chai"  && s.UnitPrice == 18m);
        Assert.Contains(summaries, s => s.ProductName == "Chang" && s.UnitPrice == 19m);
        Assert.All(summaries, s => Assert.True(s.ProductID > 0));
    }
}
