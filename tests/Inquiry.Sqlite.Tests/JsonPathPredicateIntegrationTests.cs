using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Inquiry.Entities;
using Inquiry.Sqlite.Tests.Fixtures;
using Inquiry.Stores;

namespace Inquiry.Sqlite.Tests;

[InquiryTable("JsonPathDoc")]
public sealed class JsonPathDoc
{
    [InquiryKey(IsGenerated = true)]
    public long Id { get; set; }

    [InquiryColumn("Name")]
    public string Name { get; set; } = string.Empty;

    // A plain string column holding JSON text.
    [InquiryColumn("Data")]
    public string Data { get; set; } = string.Empty;
}

public partial class JsonPathDocStore : InquiryStore<JsonPathDoc>
{
    [InquiryInsert(ReturnEntity = true)]
    public partial Task<JsonPathDoc?> InsertAsync(JsonPathDoc doc, CancellationToken cancellationToken = default);

    [InquirySelectAllByPredicate]
    [InquiryWhere("Data", Compare.Equal, JsonPath = "$.status")]
    public partial Task<IReadOnlyList<JsonPathDoc>> ByStatusAsync(string status, CancellationToken cancellationToken = default);

    [InquirySelectAllByPredicate]
    [InquiryWhere("Data", Compare.Equal, JsonPath = "$.address.city")]
    public partial Task<IReadOnlyList<JsonPathDoc>> ByCityAsync(string city, CancellationToken cancellationToken = default);

    [InquirySelectAllByPredicate]
    [InquiryWhere("Name", Compare.Like)]
    [InquiryWhere("Data", Compare.Equal, JsonPath = "$.status")]
    public partial Task<IReadOnlyList<JsonPathDoc>> ByNameAndStatusAsync(string name, string status, CancellationToken cancellationToken = default);
}

/// <summary>
/// End-to-end [InquiryWhere(JsonPath = …)] behaviour against real SQLite: a criterion filters inside a
/// JSON text column via the dialect's json_extract, supports nested paths, and AND-composes with an
/// ordinary criterion.
/// </summary>
public sealed class JsonPathPredicateIntegrationTests
{
    private const string Ddl =
        "CREATE TABLE JsonPathDoc (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL, Data TEXT NOT NULL);";

    private static async Task<(SqliteTestHarness Harness, JsonPathDocStore Store)> SeedAsync()
    {
        var harness = await SqliteTestHarness.CreateAsync(Ddl, "JsonPath");
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

        // Name LIKE 'A%' AND $.status = 'active' → only Alpha.
        var result = await store.ByNameAndStatusAsync("A%", "active");
        var only = Assert.Single(result);
        Assert.Equal("Alpha", only.Name);
    }
}
