using System.Linq;
using Inquiry.FeatureCatalog;
using Inquiry.Oracle.Tests.Fixtures;

namespace Inquiry.Oracle.Tests;

[Collection(OracleCollection.Name)]
public sealed class ViewEntityIntegrationTests
{
    private readonly OracleContainerFixture _fixture;
    public ViewEntityIntegrationTests(OracleContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task ViewStoreMaterializesAggregatedRows()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString, FeatureSchema.ViewEntityOracleDdl, "view");

        var sales = harness.GetRequiredService<SaleRowStore>();
        var totals = harness.GetRequiredService<CategoryTotalStore>();

        await sales.InsertAsync(new SaleRow { Category = "coffee", Amount = 12.50m });
        await sales.InsertAsync(new SaleRow { Category = "coffee", Amount = 7.25m });
        await sales.InsertAsync(new SaleRow { Category = "tea", Amount = 4.00m });

        var all = (await totals.AllAsync()).OrderBy(t => t.Category).ToList();

        Assert.Equal(2, all.Count);
        Assert.Equal("coffee", all[0].Category);
        Assert.Equal(2, all[0].SaleCount);
        Assert.Equal(19.75m, all[0].TotalAmount);
        Assert.Equal("tea", all[1].Category);
        Assert.Equal(1, all[1].SaleCount);
        Assert.Equal(4.00m, all[1].TotalAmount);
    }

    [SkippableFact]
    public async Task ViewStoreFiltersByField()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString, FeatureSchema.ViewEntityOracleDdl, "view");

        var sales = harness.GetRequiredService<SaleRowStore>();
        var totals = harness.GetRequiredService<CategoryTotalStore>();

        await sales.InsertAsync(new SaleRow { Category = "coffee", Amount = 10m });
        await sales.InsertAsync(new SaleRow { Category = "tea", Amount = 4m });

        var result = await totals.ByCategoryAsync("coffee");

        Assert.Single(result);
        Assert.Equal(1, result[0].SaleCount);
        Assert.Equal(10m, result[0].TotalAmount);
    }
}
