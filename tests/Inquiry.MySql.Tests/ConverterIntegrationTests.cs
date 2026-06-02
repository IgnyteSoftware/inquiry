using System.Collections.Generic;
using Inquiry.FeatureCatalog;
using Inquiry.MySql.Tests.Fixtures;

namespace Inquiry.MySql.Tests;

/// <summary>
/// W10 against real MySQL via the shared <see cref="JsonDoc"/> catalog entity: a custom value
/// converter (Money↔decimal) and a JSON column (List&lt;string&gt; serialized to text) round-trip through
/// insert and select; a null JSON value round-trips as null.
/// </summary>
[Collection(MySqlCollection.Name)]
public sealed class ConverterIntegrationTests
{
    private readonly MySqlContainerFixture _fixture;
    public ConverterIntegrationTests(MySqlContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task ConverterAndJsonColumnsRoundTrip()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, FeatureSchema.MySqlDdl, "conv");
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
        await using var harness = await MySqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, FeatureSchema.MySqlDdl, "conv");
        var store = harness.GetRequiredService<JsonDocStore>();

        await store.InsertAsync(new JsonDoc { Owner = "Grace", Balance = new Money { Amount = 0m }, Tags = null });

        var loaded = await store.GetAsync(1);
        Assert.NotNull(loaded);
        Assert.Null(loaded!.Tags);
    }
}
