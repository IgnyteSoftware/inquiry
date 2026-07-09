using System.Linq;
using Inquiry.Northwind.Models;
using Inquiry.Northwind.Stores;
using Inquiry.MariaDb.Tests.Fixtures;

namespace Inquiry.MariaDb.Tests;

/// <summary>
/// Projection over the Northwind <c>Product</c> entity against real MariaDB: a projection-returning
/// SelectAll materializes only the declared columns (ProductID, ProductName, UnitPrice) into
/// <see cref="ProductSummary"/>.
/// </summary>
[Collection(MariaDbCollection.Name)]
public sealed class ProjectionIntegrationTests
{
    private readonly MariaDbContainerFixture _fixture;
    public ProjectionIntegrationTests(MariaDbContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task ProjectionMaterializesDeclaredColumnsSubset()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MariaDbTestHarness.CreateAsync(_fixture.AdminConnectionString, "proj");
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
