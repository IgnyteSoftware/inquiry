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
}

/// <summary>End-to-end against SQLite: a custom value converter (Money↔decimal) and a JSON column
/// (List&lt;string&gt; as text) round-trip through insert and select; a null JSON value maps to NULL.</summary>
public sealed class ConverterIntegrationTests
{
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
}
