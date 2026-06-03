using System.Linq;
using Inquiry.Northwind.Models;
using Inquiry.Northwind.Stores;
using Inquiry.SqlServer.Tests.Fixtures;

namespace Inquiry.SqlServer.Tests;

/// <summary>
/// Projection over the Northwind <c>Product</c> entity against real SQL Server: a projection-returning
/// SelectAll materializes only the declared columns (ProductID, ProductName, UnitPrice) into
/// <see cref="ProductSummary"/>.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class ProjectionIntegrationTests
{
    private readonly SqlServerContainerFixture _fixture;
    public ProjectionIntegrationTests(SqlServerContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task ProjectionMaterializesDeclaredColumnsSubset()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateAsync(_fixture.AdminConnectionString, "proj");
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
