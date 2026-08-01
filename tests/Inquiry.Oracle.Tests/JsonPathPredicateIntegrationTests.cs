using System.Linq;
using Inquiry.FeatureCatalog;
using Inquiry.Oracle.Tests.Fixtures;

namespace Inquiry.Oracle.Tests;

[Collection(OracleCollection.Name)]
public sealed class JsonPathPredicateIntegrationTests
{
    private readonly OracleContainerFixture _fixture;
    public JsonPathPredicateIntegrationTests(OracleContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task FiltersByTopLevelJsonField()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString, FeatureSchema.JsonPathOracleDdl, "jsonpath");

        var store = harness.GetRequiredService<JsonPathDocStore>();

        await store.InsertAsync(new JsonPathDoc { Name = "Alpha", Data = """{"status":"active","address":{"city":"Boston"}}""" });
        await store.InsertAsync(new JsonPathDoc { Name = "Beta", Data = """{"status":"archived","address":{"city":"Boston"}}""" });
        await store.InsertAsync(new JsonPathDoc { Name = "Gamma", Data = """{"status":"active","address":{"city":"Denver"}}""" });

        var active = await store.ByStatusAsync("active");

        Assert.Equal(new[] { "Alpha", "Gamma" }, active.Select(d => d.Name).OrderBy(n => n));
    }

    [SkippableFact]
    public async Task FiltersByNestedJsonPath()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString, FeatureSchema.JsonPathOracleDdl, "jsonpath");

        var store = harness.GetRequiredService<JsonPathDocStore>();

        await store.InsertAsync(new JsonPathDoc { Name = "Alpha", Data = """{"status":"active","address":{"city":"Boston"}}""" });
        await store.InsertAsync(new JsonPathDoc { Name = "Beta", Data = """{"status":"archived","address":{"city":"Boston"}}""" });
        await store.InsertAsync(new JsonPathDoc { Name = "Gamma", Data = """{"status":"active","address":{"city":"Denver"}}""" });

        var boston = await store.ByCityAsync("Boston");

        Assert.Equal(new[] { "Alpha", "Beta" }, boston.Select(d => d.Name).OrderBy(n => n));
    }

    [SkippableFact]
    public async Task ComposesJsonPathWithOrdinaryCriterion()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString, FeatureSchema.JsonPathOracleDdl, "jsonpath");

        var store = harness.GetRequiredService<JsonPathDocStore>();

        await store.InsertAsync(new JsonPathDoc { Name = "Alpha", Data = """{"status":"active","address":{"city":"Boston"}}""" });
        await store.InsertAsync(new JsonPathDoc { Name = "Beta", Data = """{"status":"archived","address":{"city":"Boston"}}""" });
        await store.InsertAsync(new JsonPathDoc { Name = "Gamma", Data = """{"status":"active","address":{"city":"Denver"}}""" });

        var result = await store.ByNameAndStatusAsync("A%", "active");

        Assert.Single(result);
        Assert.Equal("Alpha", result[0].Name);
    }
}
