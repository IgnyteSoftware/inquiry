using System.Linq;
using Inquiry.FeatureCatalog.FullText;
using Inquiry.MariaDb.Tests.Fixtures;

namespace Inquiry.MariaDb.Tests;

/// <summary>
/// Full-text search against real MariaDB via the shared <see cref="Article"/> catalog entity. The
/// generated predicate is <c>MATCH(`Title`, `Body`) AGAINST (@searchTerm IN NATURAL LANGUAGE MODE)</c>,
/// backed by the <c>FULLTEXT</c> index in <see cref="FullTextSchema.MySqlDdl"/>.
/// </summary>
[Collection(MariaDbCollection.Name)]
public sealed class FullTextSearchIntegrationTests
{
    private readonly MariaDbContainerFixture _fixture;
    public FullTextSearchIntegrationTests(MariaDbContainerFixture fixture) => _fixture = fixture;

    private static async Task SeedAsync(MariaDbTestHarness harness)
    {
        var articles = harness.GetRequiredService<ArticleStore>();
        await articles.InsertAsync(new Article { Title = "MariaDB Guide", Body = "An introduction to database systems" });
        await articles.InsertAsync(new Article { Title = "Database Design", Body = "indexes and keys" });
        await articles.InsertAsync(new Article { Title = "Cooking", Body = "recipes and food" });
    }

    [SkippableFact]
    public async Task MatchesRowsContainingTheTerm()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MariaDbTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, FullTextSchema.MySqlDdl, "fts");
        await SeedAsync(harness);
        var articles = harness.GetRequiredService<ArticleStore>();

        // "database" appears in article 1's body and article 2's title.
        var matched = await articles.SearchAsync("database");

        Assert.Equal(2, matched.Count);
        Assert.Contains(matched, a => a.Title == "MariaDB Guide");
        Assert.Contains(matched, a => a.Title == "Database Design");
    }

    [SkippableFact]
    public async Task MatchesSingleRowByTitle()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MariaDbTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, FullTextSchema.MySqlDdl, "fts");
        await SeedAsync(harness);
        var articles = harness.GetRequiredService<ArticleStore>();

        var matched = await articles.SearchAsync("cooking");

        var only = Assert.Single(matched);
        Assert.Equal("Cooking", only.Title);
    }

    [SkippableFact]
    public async Task NoMatchReturnsEmpty()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MariaDbTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, FullTextSchema.MySqlDdl, "fts");
        await SeedAsync(harness);
        var articles = harness.GetRequiredService<ArticleStore>();

        Assert.Empty(await articles.SearchAsync("nonexistentword"));
    }
}
