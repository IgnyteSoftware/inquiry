using System.Linq;
using System.Threading.Tasks;
using Inquiry.FeatureCatalog;
using Inquiry.Sqlite.Tests.Fixtures;

namespace Inquiry.Sqlite.Tests;

/// <summary>
/// <c>[InquiryView]</c> end-to-end: a read-only store selects from a real SQLite VIEW (no DDL emitted
/// by Inquiry for it), materializing the aggregated keyless rows.
/// </summary>
public sealed class ViewEntityIntegrationTests
{
    [Fact]
    public async Task ViewStoreMaterializesAggregatedRows()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(FeatureSchema.ViewEntitySqliteDdl, "View");
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

    [Fact]
    public async Task ViewStoreFiltersByField()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(FeatureSchema.ViewEntitySqliteDdl, "View");
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
