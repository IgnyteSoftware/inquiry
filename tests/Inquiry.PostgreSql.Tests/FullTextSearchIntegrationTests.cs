using System.Linq;
using Inquiry.FeatureCatalog.FullText;
using Inquiry.PostgreSql.Tests.Fixtures;

namespace Inquiry.PostgreSql.Tests;

/// <summary>
/// Full-text search against real PostgreSQL via the shared <see cref="Article"/> catalog entity. The
/// generated predicate is <c>to_tsvector('simple', …) @@ plainto_tsquery('simple', @searchTerm)</c>, so
/// matching is exact-lexeme (the 'simple' config does not stem). This is the first live execution of
/// Inquiry's FTS path.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
public sealed class FullTextSearchIntegrationTests
{
    private readonly PostgreSqlContainerFixture _fixture;
    public FullTextSearchIntegrationTests(PostgreSqlContainerFixture fixture) => _fixture = fixture;

    private static async Task SeedAsync(PostgreSqlTestHarness harness)
    {
        var articles = harness.GetRequiredService<ArticleStore>();
        await articles.InsertAsync(new Article { Title = "PostgreSQL Guide", Body = "An introduction to database systems" });
        await articles.InsertAsync(new Article { Title = "Database Design", Body = "indexes and keys" });
        await articles.InsertAsync(new Article { Title = "Cooking", Body = "recipes and food" });
    }

    [SkippableFact]
    public async Task MatchesRowsContainingTheTerm()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await PostgreSqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, FullTextSchema.PostgreSqlDdl, "fts");
        await SeedAsync(harness);
        var articles = harness.GetRequiredService<ArticleStore>();

        // "database" appears in article 1's body and article 2's title.
        var matched = await articles.SearchAsync("database");

        Assert.Equal(2, matched.Count);
        Assert.Contains(matched, a => a.Title == "PostgreSQL Guide");
        Assert.Contains(matched, a => a.Title == "Database Design");
    }

    [SkippableFact]
    public async Task MatchesSingleRowByTitle()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await PostgreSqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, FullTextSchema.PostgreSqlDdl, "fts");
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
        await using var harness = await PostgreSqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, FullTextSchema.PostgreSqlDdl, "fts");
        await SeedAsync(harness);
        var articles = harness.GetRequiredService<ArticleStore>();

        Assert.Empty(await articles.SearchAsync("nonexistentword"));
    }
}
