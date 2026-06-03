using System.Collections.Generic;
using Inquiry.FeatureCatalog;
using Inquiry.SqlServer.Tests.Fixtures;

namespace Inquiry.SqlServer.Tests;

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
}
