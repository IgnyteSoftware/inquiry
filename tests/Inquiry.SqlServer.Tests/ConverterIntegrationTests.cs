using System.Collections.Generic;
using Inquiry.Entities;
using Inquiry.FeatureCatalog;
using Inquiry.SqlServer.Tests.Fixtures;
using Inquiry.Stores;

namespace Inquiry.SqlServer.Tests;

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

/// <summary>
/// Value converters against real SQL Server via the shared <see cref="JsonDoc"/> catalog entity: a custom value
/// converter (Money↔decimal) and a JSON column (List&lt;string&gt; serialized to text) round-trip through
/// insert and select; a null JSON value round-trips as null.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class ConverterIntegrationTests
{
    private readonly SqlServerContainerFixture _fixture;
    public ConverterIntegrationTests(SqlServerContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task ConverterAndJsonColumnsRoundTrip()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, FeatureSchema.SqlServerDdl, "conv");
        var store = harness.GetRequiredService<JsonDocStore>();

        await store.InsertAsync(new JsonDoc
        {
            Owner = "Ada",
            Balance = new Money { Amount = 12.50m },
            Tags = new List<string> { "savings", "primary" },
        });

        var loaded = await store.GetAsync(1);
        Assert.NotNull(loaded);
        Assert.Equal(12.50m, loaded!.Balance.Amount);
        Assert.Equal(new[] { "savings", "primary" }, loaded.Tags);
    }

    [SkippableFact]
    public async Task NullJsonColumnRoundTripsAsNull()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, FeatureSchema.SqlServerDdl, "conv");
        var store = harness.GetRequiredService<JsonDocStore>();

        await store.InsertAsync(new JsonDoc { Owner = "Grace", Balance = new Money { Amount = 0m }, Tags = null });

        var loaded = await store.GetAsync(1);
        Assert.NotNull(loaded);
        Assert.Null(loaded!.Tags);
    }

    [SkippableFact]
    public async Task UnsignedProviderTypeConverterRoundTripsEdgeValue()
    {
        // #92: a converter whose provider type is uint round-trips an edge value past int.MaxValue on
        // SQL Server. Before the fix the write threw OverflowException (checked Convert.ToInt32(uint)) and
        // the read threw InvalidCastException (GetFieldValue<uint>). Stored via the signed INT partner.
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        const string gaugeDdl = "CREATE TABLE Gauge (Id BIGINT IDENTITY(1,1) PRIMARY KEY, Ticks INT NOT NULL);";
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, gaugeDdl, "gaugeconv");
        var store = harness.GetRequiredService<GaugeStore>();

        await store.InsertAsync(new Gauge { Ticks = new Counter { Value = 3_000_000_000u } }); // > int.MaxValue
        var loaded = await store.GetAsync(1);

        Assert.NotNull(loaded);
        Assert.Equal(3_000_000_000u, loaded!.Ticks.Value);
    }
}
