using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Inquiry.Entities;
using Inquiry.Sqlite.Tests.Fixtures;
using Inquiry.Stores;

namespace Inquiry.Sqlite.Tests;

[InquiryTable("TopSale")]
public sealed class TopSale
{
    [InquiryKey(IsGenerated = true)]
    public long Id { get; set; }

    [InquiryColumn("Amount")]
    public decimal Amount { get; set; }

    [InquiryColumn("Region")]
    public string Region { get; set; } = string.Empty;
}

public partial class TopSaleStore : InquiryStore<TopSale>
{
    [InquiryInsert]
    public partial Task<int> InsertAsync(TopSale sale, CancellationToken cancellationToken = default);

    [InquirySelectTopByOrder("Amount")]
    public partial Task<TopSale?> GetCheapestAsync(CancellationToken cancellationToken = default);

    [InquirySelectTopByOrder("Amount", Descending = true)]
    public partial Task<TopSale?> GetMostExpensiveAsync(CancellationToken cancellationToken = default);
}

public sealed class TopByOrderIntegrationTests
{
    private const string Ddl = "CREATE TABLE TopSale (Id INTEGER PRIMARY KEY AUTOINCREMENT, Amount DECIMAL NOT NULL, Region TEXT NOT NULL);";

    [Fact]
    public async Task GetCheapestReturnsLowestAmountRow()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Ddl, "TopByOrder");
        var store = harness.GetRequiredService<TopSaleStore>();

        await store.InsertAsync(new TopSale { Amount = 50m, Region = "East" });
        await store.InsertAsync(new TopSale { Amount = 10m, Region = "West" });
        await store.InsertAsync(new TopSale { Amount = 30m, Region = "East" });

        var cheapest = await store.GetCheapestAsync();
        Assert.NotNull(cheapest);
        Assert.Equal(10m, cheapest.Amount);
        Assert.Equal("West", cheapest.Region);
    }

    [Fact]
    public async Task GetMostExpensiveReturnsHighestAmountRow()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Ddl, "TopByOrder");
        var store = harness.GetRequiredService<TopSaleStore>();

        await store.InsertAsync(new TopSale { Amount = 50m, Region = "East" });
        await store.InsertAsync(new TopSale { Amount = 10m, Region = "West" });
        await store.InsertAsync(new TopSale { Amount = 30m, Region = "East" });

        var expensive = await store.GetMostExpensiveAsync();
        Assert.NotNull(expensive);
        Assert.Equal(50m, expensive.Amount);
    }

    [Fact]
    public async Task GetCheapestReturnsNullOnEmptyTable()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Ddl, "TopByOrder");
        var store = harness.GetRequiredService<TopSaleStore>();

        var result = await store.GetCheapestAsync();
        Assert.Null(result);
    }
}
