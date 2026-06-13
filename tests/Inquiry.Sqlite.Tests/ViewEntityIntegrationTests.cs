using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Inquiry.Entities;
using Inquiry.Sqlite.Tests.Fixtures;
using Inquiry.Stores;

namespace Inquiry.Sqlite.Tests;

[InquiryTable("Sale")]
public sealed class SaleRow
{
    [InquiryKey(IsGenerated = true)]
    public long Id { get; set; }

    [InquiryColumn]
    public string Category { get; set; } = string.Empty;

    [InquiryColumn]
    public decimal Amount { get; set; }
}

public partial class SaleRowStore : InquiryStore<SaleRow>
{
    [InquiryInsert]
    public partial Task<int> InsertAsync(SaleRow row, CancellationToken cancellationToken = default);
}

/// <summary>A keyless, read-only entity mapped to a database VIEW that aggregates the Sale table.</summary>
[InquiryView("v_CategoryTotals")]
public sealed class CategoryTotal
{
    [InquiryColumn("Category")]
    public string Category { get; set; } = string.Empty;

    [InquiryColumn("SaleCount")]
    public int SaleCount { get; set; }

    [InquiryColumn("TotalAmount")]
    public decimal TotalAmount { get; set; }
}

public partial class CategoryTotalStore : InquiryStore<CategoryTotal>
{
    [InquirySelectAll]
    public partial Task<IReadOnlyList<CategoryTotal>> AllAsync(CancellationToken cancellationToken = default);

    [InquirySelectAllByField(nameof(CategoryTotal.Category))]
    public partial Task<IReadOnlyList<CategoryTotal>> ByCategoryAsync(string category, CancellationToken cancellationToken = default);
}

/// <summary>
/// <c>[InquiryView]</c> end-to-end: a read-only store selects from a real SQLite VIEW (no DDL emitted
/// by Inquiry for it), materializing the aggregated keyless rows.
/// </summary>
public sealed class ViewEntityIntegrationTests
{
    private const string Ddl = """
        CREATE TABLE Sale (Id INTEGER PRIMARY KEY AUTOINCREMENT, Category TEXT NOT NULL, Amount NUMERIC NOT NULL);
        CREATE VIEW v_CategoryTotals AS
            SELECT Category, COUNT(*) AS SaleCount, SUM(Amount) AS TotalAmount
            FROM Sale GROUP BY Category;
        """;

    [Fact]
    public async Task ViewStoreMaterializesAggregatedRows()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Ddl, "View");
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
        await using var harness = await SqliteTestHarness.CreateAsync(Ddl, "View");
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
