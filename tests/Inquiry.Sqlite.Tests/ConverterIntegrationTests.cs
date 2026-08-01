using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Inquiry.Entities;
using Inquiry.Sqlite.Tests.Fixtures;
using Inquiry.Stores;

namespace Inquiry.Sqlite.Tests;

public readonly struct Money
{
    public decimal Amount { get; init; }
}

public sealed class MoneyConverter : IInquiryValueConverter<Money, decimal>
{
    public decimal ToProvider(Money model) => model.Amount;
    public Money FromProvider(decimal provider) => new() { Amount = provider };
}

[InquiryTable("Wallet")]
public sealed class Wallet
{
    [InquiryKey(IsGenerated = true)]
    public long Id { get; set; }

    [InquiryColumn("Owner")]
    public string Owner { get; set; } = string.Empty;

    [InquiryColumn("Balance", Converter = typeof(MoneyConverter))]
    public Money Balance { get; set; }

    [InquiryColumn("Tags"), InquiryJson]
    public List<string>? Tags { get; set; }
}

public partial class WalletStore : InquiryStore<Wallet>
{
    [InquiryInsert]
    public partial Task<int> InsertAsync(Wallet wallet, CancellationToken cancellationToken = default);

    [InquirySelectOneByKey]
    public partial Task<Wallet?> GetAsync(long id, CancellationToken cancellationToken = default);

    [InquirySelectAllByPredicate]
    [InquiryWhere(nameof(Wallet.Balance), Compare.In)]
    public partial Task<IReadOnlyList<Wallet>> ByBalancesAsync(IReadOnlyList<Money> balances, CancellationToken cancellationToken = default);

    [InquirySelectAllByPredicate]
    [InquiryWhere(nameof(Wallet.Balance), Compare.NotIn)]
    public partial Task<IReadOnlyList<Wallet>> ExcludeBalancesAsync(IReadOnlyList<Money> balances, CancellationToken cancellationToken = default);
}

public readonly struct Counter
{
    public uint Value { get; init; }
}

public sealed class CounterConverter : IInquiryValueConverter<Counter, uint>
{
    public uint ToProvider(Counter model) => model.Value;
    public Counter FromProvider(uint provider) => new() { Value = provider };
}

[InquiryTable("Gauge")]
public sealed class Gauge
{
    [InquiryKey(IsGenerated = true)]
    public long Id { get; set; }

    [InquiryColumn("Ticks", Converter = typeof(CounterConverter))]
    public Counter Ticks { get; set; }
}

public partial class GaugeStore : InquiryStore<Gauge>
{
    [InquiryInsert]
    public partial Task<int> InsertAsync(Gauge gauge, CancellationToken cancellationToken = default);

    [InquirySelectOneByKey]
    public partial Task<Gauge?> GetAsync(long id, CancellationToken cancellationToken = default);
}

/// <summary>End-to-end against SQLite: a custom value converter (Money↔decimal) and a JSON column
/// (List&lt;string&gt; as text) round-trip through insert and select; a null JSON value maps to NULL.</summary>
public sealed class ConverterIntegrationTests
{
    /// <summary>#92: a converter whose provider type is unsigned (uint) round-trips an edge value past
    /// int.MaxValue — write reinterprets uint→int into the signed storage column, read reinterprets back.</summary>
    [Fact]
    public async Task UnsignedProviderTypeConverterRoundTripsEdgeValue()
    {
        const string gaugeDdl = "CREATE TABLE Gauge (Id INTEGER PRIMARY KEY AUTOINCREMENT, Ticks INTEGER NOT NULL);";
        await using var harness = await SqliteTestHarness.CreateAsync(gaugeDdl, "GaugeConv");
        var store = harness.GetRequiredService<GaugeStore>();

        await store.InsertAsync(new Gauge { Ticks = new Counter { Value = 3_000_000_000u } }); // > int.MaxValue
        var loaded = await store.GetAsync(1);

        Assert.NotNull(loaded);
        Assert.Equal(3_000_000_000u, loaded!.Ticks.Value);
    }

    private const string Ddl = "CREATE TABLE Wallet (Id INTEGER PRIMARY KEY AUTOINCREMENT, Owner TEXT NOT NULL, Balance NUMERIC NOT NULL, Tags TEXT NULL);";

    [Fact]
    public async Task ConverterAndJsonColumnsRoundTrip()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Ddl, "Converter");
        var store = harness.GetRequiredService<WalletStore>();

        await store.InsertAsync(new Wallet
        {
            Owner = "Ada",
            Balance = new Money { Amount = 12.50m },
            Tags = new List<string> { "savings", "primary" },
        });

        var loaded = await store.GetAsync(1);
        Assert.NotNull(loaded);
        Assert.Equal(12.50m, loaded!.Balance.Amount);
        Assert.Equal(new[] { "savings", "primary" }, loaded.Tags);

        // The JSON column stores text.
        var rawTags = await harness.ExecuteScalarAsync("SELECT Tags FROM Wallet LIMIT 1");
        Assert.Equal("[\"savings\",\"primary\"]", rawTags);
    }

    [Fact]
    public async Task NullJsonColumnRoundTripsAsNull()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Ddl, "Converter");
        var store = harness.GetRequiredService<WalletStore>();

        await store.InsertAsync(new Wallet { Owner = "Grace", Balance = new Money { Amount = 0m }, Tags = null });

        Assert.Null(await harness.ExecuteScalarAsync("SELECT Tags FROM Wallet LIMIT 1"));
        var loaded = await store.GetAsync(1);
        Assert.NotNull(loaded);
        Assert.Null(loaded!.Tags);
    }

    /// <summary>Reproduces bug #51: IN over a converter column must call ToProvider on each element.</summary>
    [Fact]
    public async Task InPredicateFiltersConverterColumnViaToProvider()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Ddl, "Converter");
        var store = harness.GetRequiredService<WalletStore>();

        await store.InsertAsync(new Wallet { Owner = "Ada", Balance = new Money { Amount = 10m } });
        await store.InsertAsync(new Wallet { Owner = "Grace", Balance = new Money { Amount = 20m } });
        await store.InsertAsync(new Wallet { Owner = "Alan", Balance = new Money { Amount = 30m } });

        // Without the fix, the raw Money struct is passed to the driver → never matches the stored decimal → 0 rows.
        var result = await store.ByBalancesAsync(new[] { new Money { Amount = 10m }, new Money { Amount = 30m } });
        Assert.Equal(2, result.Count);
        Assert.Contains(result, w => w.Owner == "Ada");
        Assert.Contains(result, w => w.Owner == "Alan");
    }

    /// <summary>Reproduces bug #51 for NOT IN: must also call ToProvider on each element.</summary>
    [Fact]
    public async Task NotInPredicateFiltersConverterColumnViaToProvider()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Ddl, "Converter");
        var store = harness.GetRequiredService<WalletStore>();

        await store.InsertAsync(new Wallet { Owner = "Ada", Balance = new Money { Amount = 10m } });
        await store.InsertAsync(new Wallet { Owner = "Grace", Balance = new Money { Amount = 20m } });
        await store.InsertAsync(new Wallet { Owner = "Alan", Balance = new Money { Amount = 30m } });

        // NOT IN([10, 30]) should return only the 20m row.
        var result = await store.ExcludeBalancesAsync(new[] { new Money { Amount = 10m }, new Money { Amount = 30m } });
        Assert.Single(result);
        Assert.Equal("Grace", result[0].Owner);
    }
}
