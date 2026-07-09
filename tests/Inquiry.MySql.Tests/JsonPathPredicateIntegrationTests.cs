using System.Linq;
using System.Threading.Tasks;
using Inquiry.FeatureCatalog;
using Inquiry.MySql.Tests.Fixtures;

namespace Inquiry.MySql.Tests;

/// <summary>
/// End-to-end <c>[InquiryWhere(JsonPath = ...)]</c> behaviour against real MySQL: a criterion filters
/// inside a JSON text column via the dialect's JSON_EXTRACT, supports nested paths, and AND-composes
/// with an ordinary criterion.
/// </summary>
[Collection(MySqlCollection.Name)]
public sealed class JsonPathPredicateIntegrationTests
{
    private readonly MySqlContainerFixture _fixture;
    public JsonPathPredicateIntegrationTests(MySqlContainerFixture fixture) => _fixture = fixture;

    private async Task<(MySqlTestHarness Harness, JsonPathDocStore Store)> SeedAsync()
    {
        var harness = await MySqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, FeatureSchema.JsonPathMySqlDdl, "jsonpath");
        var store = harness.GetRequiredService<JsonPathDocStore>();
        await store.InsertAsync(new JsonPathDoc { Name = "Alpha", Data = """{"status":"active","address":{"city":"Boston"}}""" });
        await store.InsertAsync(new JsonPathDoc { Name = "Beta", Data = """{"status":"archived","address":{"city":"Boston"}}""" });
        await store.InsertAsync(new JsonPathDoc { Name = "Gamma", Data = """{"status":"active","address":{"city":"Denver"}}""" });
        return (harness, store);
    }

    [SkippableFact]
    public async Task FiltersByTopLevelJsonField()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        var (harness, store) = await SeedAsync();
        await using var _ = harness;

        var active = await store.ByStatusAsync("active");
        Assert.Equal(new[] { "Alpha", "Gamma" }, active.Select(d => d.Name).OrderBy(n => n).ToArray());
    }

    [SkippableFact]
    public async Task FiltersByNestedJsonPath()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        var (harness, store) = await SeedAsync();
        await using var _ = harness;

        var boston = await store.ByCityAsync("Boston");
        Assert.Equal(new[] { "Alpha", "Beta" }, boston.Select(d => d.Name).OrderBy(n => n).ToArray());
    }

    [SkippableFact]
    public async Task ComposesJsonPathWithOrdinaryCriterion()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        var (harness, store) = await SeedAsync();
        await using var _ = harness;

        var result = await store.ByNameAndStatusAsync("A%", "active");
        var only = Assert.Single(result);
        Assert.Equal("Alpha", only.Name);
    }
}
