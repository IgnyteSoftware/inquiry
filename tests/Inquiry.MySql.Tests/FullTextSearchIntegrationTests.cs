using System.Linq;
using Inquiry.FeatureCatalog.FullText;
using Inquiry.MySql.Tests.Fixtures;

namespace Inquiry.MySql.Tests;

/// <summary>
/// Full-text search against real MySQL via the shared <see cref="Article"/> catalog entity. The
/// generated predicate is <c>MATCH(`Title`, `Body`) AGAINST (@searchTerm IN NATURAL LANGUAGE MODE)</c>,
/// backed by the <c>FULLTEXT</c> index in <see cref="FullTextSchema.MySqlDdl"/>.
/// </summary>
[Collection(MySqlCollection.Name)]
public sealed class FullTextSearchIntegrationTests
{
    private readonly MySqlContainerFixture _fixture;
    public FullTextSearchIntegrationTests(MySqlContainerFixture fixture) => _fixture = fixture;

    private static async Task SeedAsync(MySqlTestHarness harness)
    {
        var articles = harness.GetRequiredService<ArticleStore>();
        await articles.InsertAsync(new Article { Title = "MySQL Guide", Body = "An introduction to database systems" });
        await articles.InsertAsync(new Article { Title = "Database Design", Body = "indexes and keys" });
        await articles.InsertAsync(new Article { Title = "Cooking", Body = "recipes and food" });
    }

    [SkippableFact]
    public async Task MatchesRowsContainingTheTerm()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, FullTextSchema.MySqlDdl, "fts");
        await SeedAsync(harness);
        var articles = harness.GetRequiredService<ArticleStore>();

        // "database" appears in article 1's body and article 2's title.
        var matched = await articles.SearchAsync("database");

        Assert.Equal(2, matched.Count);
        Assert.Contains(matched, a => a.Title == "MySQL Guide");
        Assert.Contains(matched, a => a.Title == "Database Design");
    }

    [SkippableFact]
    public async Task MatchesSingleRowByTitle()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, FullTextSchema.MySqlDdl, "fts");
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
        await using var harness = await MySqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, FullTextSchema.MySqlDdl, "fts");
        await SeedAsync(harness);
        var articles = harness.GetRequiredService<ArticleStore>();

        Assert.Empty(await articles.SearchAsync("nonexistentword"));
    }
}
