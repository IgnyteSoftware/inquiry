using Inquiry.FeatureCatalog;
using Inquiry.Oracle.Tests.Fixtures;

namespace Inquiry.Oracle.Tests;

/// <summary>
/// The <c>SqlType = "JSON"</c> opt-in against a native JSON column on real Oracle: an <c>[InquiryJson]</c>
/// property and a plain string property round-trip through every write path and a JSON-path predicate.
/// </summary>
[Collection(OracleCollection.Name)]
public sealed class NativeJsonColumnIntegrationTests
{
    private readonly OracleContainerFixture _fixture;
    public NativeJsonColumnIntegrationTests(OracleContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task SingleRowWritesAndPredicatesRoundTripNativeJsonColumns()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, FeatureSchema.NativeJsonOracleDdl, "nativejson");
        var store = harness.GetRequiredService<NativeJsonDocStore>();

        Assert.Equal(1, await store.InsertAsync(NativeJsonDoc.Create(1, "dark", "Boston")));
        Assert.Equal(1, await store.UpsertAsync(NativeJsonDoc.Create(2, "light", "Boston")));
        AssertDoc(await store.GetAsync(1), "dark", "Boston");
        AssertDoc(await store.GetAsync(2), "light", "Boston");

        Assert.True(await store.UpdateAsync(NativeJsonDoc.Create(1, "solar", "Austin")));
        await store.UpsertAsync(NativeJsonDoc.Create(2, "lunar", "Denver"));
        AssertDoc(await store.GetAsync(1), "solar", "Austin");
        AssertDoc(await store.GetAsync(2), "lunar", "Denver");

        Assert.Equal(1, await store.SetSettingsAsync(new NativeJsonSettings { Theme = "dim" }, 2));
        Assert.Equal("dim", (await store.GetAsync(2))?.Settings?.Theme);
        Assert.Equal([1], (await store.ByPayloadCityAsync("Austin")).Select(static d => d.Id));

        Assert.True(await store.UpdateAsync(new NativeJsonDoc { Id = 1 }));
        var cleared = await store.GetAsync(1);
        Assert.NotNull(cleared);
        Assert.Null(cleared.Settings);
        Assert.Null(cleared.Payload);
    }

    [SkippableFact]
    public async Task BatchAndBulkWritesRoundTripNativeJsonColumns()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, FeatureSchema.NativeJsonOracleDdl, "nativejsonbatch");
        var store = harness.GetRequiredService<NativeJsonDocStore>();

        Assert.Equal(2, await store.InsertAllAsync([NativeJsonDoc.Create(1, "dark", "Boston"), NativeJsonDoc.Create(2, "light", "Denver")]));
        Assert.Equal(2, await store.BulkInsertAsync([NativeJsonDoc.Create(3, "dim", "Austin"), new NativeJsonDoc { Id = 4 }]));
        Assert.Equal(2, await store.UpdateAllAsync([NativeJsonDoc.Create(1, "solar", "Reno"), NativeJsonDoc.Create(2, "lunar", "Omaha")]));

        AssertDoc(await store.GetAsync(1), "solar", "Reno");
        AssertDoc(await store.GetAsync(2), "lunar", "Omaha");
        AssertDoc(await store.GetAsync(3), "dim", "Austin");
        var empty = await store.GetAsync(4);
        Assert.NotNull(empty);
        Assert.Null(empty.Settings);
        Assert.Null(empty.Payload);
    }

    private static void AssertDoc(NativeJsonDoc? doc, string theme, string city)
    {
        Assert.NotNull(doc);
        Assert.Equal(theme, doc.Settings?.Theme);
        Assert.Equal(city, doc.Settings?.Address?.City);
        Assert.Contains(city, doc.Payload, StringComparison.Ordinal);
    }
}
