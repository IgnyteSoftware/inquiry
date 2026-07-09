using System.Linq;
using Inquiry.FeatureCatalog;
using Inquiry.SqlServer.Tests.Fixtures;

namespace Inquiry.SqlServer.Tests;

/// <summary>
/// <c>[InquiryView]</c> end-to-end against real SQL Server: a read-only store selects from a real VIEW
/// (no DDL emitted by Inquiry for it), materializing the aggregated keyless rows.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class ViewEntityIntegrationTests
{
    private readonly SqlServerContainerFixture _fixture;
    public ViewEntityIntegrationTests(SqlServerContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task ViewStoreMaterializesAggregatedRows()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, FeatureSchema.ViewEntitySqlServerDdl, "view");
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
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, FeatureSchema.ViewEntitySqlServerDdl, "view");
        var sales = harness.GetRequiredService<SaleRowStore>();
        var totals = harness.GetRequiredService<CategoryTotalStore>();

        await sales.InsertAsync(new SaleRow { Category = "coffee", Amount = 10m });
        await sales.InsertAsync(new SaleRow { Category = "tea", Amount = 4m });

        var coffee = Assert.Single(await totals.ByCategoryAsync("coffee"));
        Assert.Equal("coffee", coffee.Category);
        Assert.Equal(1, coffee.SaleCount);
        Assert.Equal(10m, coffee.TotalAmount);
    }
}
