using System.Threading;
using System.Threading.Tasks;
using Inquiry.Entities;
using Inquiry.Sqlite.Tests.Fixtures;
using Inquiry.Stores;

namespace Inquiry.Sqlite.Tests;

[InquiryTable("Sale")]
public sealed class Sale
{
    [InquiryKey(IsGenerated = true)]
    public long Id { get; set; }

    [InquiryColumn("Amount")]
    public decimal Amount { get; set; }
}

public partial class SaleStore : InquiryStore<Sale>
{
    [InquiryInsert]
    public partial Task<int> InsertAsync(Sale sale, CancellationToken cancellationToken = default);

    [InquiryCount]
    public partial Task<long> CountAsync(CancellationToken cancellationToken = default);

    [InquiryAggregate(InquiryAggregateFunction.Sum, "Amount")]
    public partial Task<decimal?> SumAmountAsync(CancellationToken cancellationToken = default);

    [InquiryAggregate(InquiryAggregateFunction.Max, "Amount")]
    public partial Task<decimal?> MaxAmountAsync(CancellationToken cancellationToken = default);
}

/// <summary>W5 aggregates end-to-end against SQLite: COUNT, SUM, MAX, and a null aggregate over no rows.</summary>
public sealed class AggregateIntegrationTests
{
    private const string Ddl = "CREATE TABLE Sale (Id INTEGER PRIMARY KEY AUTOINCREMENT, Amount NUMERIC NOT NULL);";

    private static async Task<SqliteTestHarness> SeedAsync()
    {
        var harness = await SqliteTestHarness.CreateAsync(Ddl, "Aggregate");
        var store = harness.GetRequiredService<SaleStore>();
        await store.InsertAsync(new Sale { Amount = 10m });
        await store.InsertAsync(new Sale { Amount = 20m });
        await store.InsertAsync(new Sale { Amount = 30m });
        return harness;
    }

    [Fact]
    public async Task ComputesCountSumMax()
    {
        await using var harness = await SeedAsync();
        var store = harness.GetRequiredService<SaleStore>();

        Assert.Equal(3L, await store.CountAsync());
        Assert.Equal(60m, await store.SumAmountAsync());
        Assert.Equal(30m, await store.MaxAmountAsync());
    }

    [Fact]
    public async Task NullAggregateOverNoRows()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Ddl, "Aggregate");
        var store = harness.GetRequiredService<SaleStore>();

        Assert.Equal(0L, await store.CountAsync());
        Assert.Null(await store.SumAmountAsync());
    }
}
