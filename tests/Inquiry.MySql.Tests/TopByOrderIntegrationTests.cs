using Inquiry.Entities;
using Inquiry.MySql.Tests.Fixtures;
using Inquiry.Stores;

namespace Inquiry.MySql.Tests;

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

[Collection(MySqlCollection.Name)]
public sealed class TopByOrderIntegrationTests
{
    private readonly MySqlContainerFixture _fixture;
    public TopByOrderIntegrationTests(MySqlContainerFixture fixture) => _fixture = fixture;

    private const string Ddl = "CREATE TABLE `TopSale` (`Id` BIGINT AUTO_INCREMENT PRIMARY KEY, `Amount` DECIMAL(18,2) NOT NULL, `Region` VARCHAR(255) NOT NULL);";

    [SkippableFact]
    public async Task GetCheapestReturnsLowestAmountRow()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "toporder");
        var store = harness.GetRequiredService<TopSaleStore>();

        await store.InsertAsync(new TopSale { Amount = 50m, Region = "East" });
        await store.InsertAsync(new TopSale { Amount = 10m, Region = "West" });
        await store.InsertAsync(new TopSale { Amount = 30m, Region = "East" });

        var cheapest = await store.GetCheapestAsync();
        Assert.NotNull(cheapest);
        Assert.Equal(10m, cheapest.Amount);
        Assert.Equal("West", cheapest.Region);
    }

    [SkippableFact]
    public async Task GetMostExpensiveReturnsHighestAmountRow()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "toporder");
        var store = harness.GetRequiredService<TopSaleStore>();

        await store.InsertAsync(new TopSale { Amount = 50m, Region = "East" });
        await store.InsertAsync(new TopSale { Amount = 10m, Region = "West" });
        await store.InsertAsync(new TopSale { Amount = 30m, Region = "East" });

        var expensive = await store.GetMostExpensiveAsync();
        Assert.NotNull(expensive);
        Assert.Equal(50m, expensive.Amount);
    }

    [SkippableFact]
    public async Task GetCheapestReturnsNullOnEmptyTable()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "toporder");
        var store = harness.GetRequiredService<TopSaleStore>();

        var result = await store.GetCheapestAsync();
        Assert.Null(result);
    }
}
