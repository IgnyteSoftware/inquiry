using System.Linq;
using System.Threading.Tasks;
using Inquiry.FeatureCatalog;
using Inquiry.Sqlite.Tests.Fixtures;

namespace Inquiry.Sqlite.Tests;

/// <summary>
/// End-to-end [InquiryWhere(JsonPath = …)] behaviour against real SQLite: a criterion filters inside a
/// JSON text column via the dialect's json_extract, supports nested paths, and AND-composes with an
/// ordinary criterion.
/// </summary>
public sealed class JsonPathPredicateIntegrationTests
{
    private static async Task<(SqliteTestHarness Harness, JsonPathDocStore Store)> SeedAsync()
    {
        var harness = await SqliteTestHarness.CreateAsync(FeatureSchema.JsonPathSqliteDdl, "JsonPath");
        var store = harness.GetRequiredService<JsonPathDocStore>();
        await store.InsertAsync(new JsonPathDoc { Name = "Alpha", Data = """{"status":"active","address":{"city":"Boston"}}""" });
        await store.InsertAsync(new JsonPathDoc { Name = "Beta", Data = """{"status":"archived","address":{"city":"Boston"}}""" });
        await store.InsertAsync(new JsonPathDoc { Name = "Gamma", Data = """{"status":"active","address":{"city":"Denver"}}""" });
        return (harness, store);
    }

    [Fact]
    public async Task FiltersByTopLevelJsonField()
    {
        var (harness, store) = await SeedAsync();
        await using var _ = harness;

        var active = await store.ByStatusAsync("active");
        Assert.Equal(new[] { "Alpha", "Gamma" }, active.Select(d => d.Name).OrderBy(n => n).ToArray());
    }

    [Fact]
    public async Task FiltersByNestedJsonPath()
    {
        var (harness, store) = await SeedAsync();
        await using var _ = harness;

        var boston = await store.ByCityAsync("Boston");
        Assert.Equal(new[] { "Alpha", "Beta" }, boston.Select(d => d.Name).OrderBy(n => n).ToArray());
    }

    [Fact]
    public async Task ComposesJsonPathWithOrdinaryCriterion()
    {
        var (harness, store) = await SeedAsync();
        await using var _ = harness;

        var result = await store.ByNameAndStatusAsync("A%", "active");
        var only = Assert.Single(result);
        Assert.Equal("Alpha", only.Name);
    }
}
