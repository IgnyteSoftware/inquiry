using System.Linq;
using System.Threading.Tasks;
using Inquiry.FeatureCatalog.FullText;
using Inquiry.SqlServer.Tests.Fixtures;

namespace Inquiry.SqlServer.Tests;

/// <summary>
/// W9 full-text search against real SQL Server via the shared <see cref="Article"/> catalog entity. The
/// generated predicate is <c>FREETEXT(([Title],[Body]), @searchTerm)</c>. SQL Server full-text search
/// requires the optional full-text engine component plus a full-text catalog/index with ASYNC population;
/// the default test container usually lacks the engine, so each test SKIPS cleanly (never fails) when FTS
/// is unavailable.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class FullTextSearchIntegrationTests
{
    private readonly SqlServerContainerFixture _fixture;
    public FullTextSearchIntegrationTests(SqlServerContainerFixture fixture) => _fixture = fixture;

    private static async Task SeedAsync(SqlServerTestHarness harness)
    {
        var articles = harness.GetRequiredService<ArticleStore>();
        await articles.InsertAsync(new Article { Title = "SQL Server Guide", Body = "An introduction to database systems" });
        await articles.InsertAsync(new Article { Title = "Database Design", Body = "indexes and keys" });
        await articles.InsertAsync(new Article { Title = "Cooking", Body = "recipes and food" });
    }

    // Builds the FTS catalog + index, skipping the test cleanly if the full-text engine is unavailable.
    private static async Task SetUpFullTextOrSkipAsync(SqlServerTestHarness harness)
    {
        try
        {
            await using var connection = new Microsoft.Data.SqlClient.SqlConnection(harness.ConnectionString);
            await connection.OpenAsync();

            await using (var check = connection.CreateCommand())
            {
                check.CommandText = "SELECT FULLTEXTSERVICEPROPERTY('IsFullTextInstalled')";
                var installed = await check.ExecuteScalarAsync();
                if (installed is null || installed == System.DBNull.Value || System.Convert.ToInt32(installed) == 0)
                {
                    Skip.If(true, "SQL Server full-text component not installed (IsFullTextInstalled = 0).");
                    return;
                }
            }

            await using (var catalog = connection.CreateCommand())
            {
                catalog.CommandText = FullTextSchema.SqlServerCreateCatalog;
                await catalog.ExecuteNonQueryAsync();
            }

            await using (var index = connection.CreateCommand())
            {
                index.CommandText = FullTextSchema.SqlServerCreateIndex;
                await index.ExecuteNonQueryAsync();
            }
        }
        catch (System.Exception ex)
        {
            Skip.If(true, "SQL Server full-text component not available: " + ex.Message);
            return;
        }
    }

    // Full-text indexes populate asynchronously; wait until the population status returns to idle (0).
    private static async Task WaitForPopulationAsync(SqlServerTestHarness harness)
    {
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(harness.ConnectionString);
        await connection.OpenAsync();

        var deadline = System.DateTime.UtcNow.AddSeconds(15);
        while (System.DateTime.UtcNow < deadline)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT OBJECTPROPERTYEX(OBJECT_ID('Article'),'TableFulltextPopulateStatus')";
            var status = await cmd.ExecuteScalarAsync();
            if (status is not null && status != System.DBNull.Value && System.Convert.ToInt32(status) == 0)
            {
                return;
            }

            await Task.Delay(250);
        }
    }

    [SkippableFact]
    public async Task MatchesRowsContainingTheTerm()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, FullTextSchema.SqlServerDdl, "fts");
        await SetUpFullTextOrSkipAsync(harness);
        await SeedAsync(harness);
        await WaitForPopulationAsync(harness);
        var articles = harness.GetRequiredService<ArticleStore>();

        // "database" appears in article 1's body and article 2's title.
        var matched = await articles.SearchAsync("database");

        Assert.Equal(2, matched.Count);
        Assert.Contains(matched, a => a.Title == "SQL Server Guide");
        Assert.Contains(matched, a => a.Title == "Database Design");
    }

    [SkippableFact]
    public async Task MatchesSingleRowByTitle()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, FullTextSchema.SqlServerDdl, "fts");
        await SetUpFullTextOrSkipAsync(harness);
        await SeedAsync(harness);
        await WaitForPopulationAsync(harness);
        var articles = harness.GetRequiredService<ArticleStore>();

        var matched = await articles.SearchAsync("cooking");

        var only = Assert.Single(matched);
        Assert.Equal("Cooking", only.Title);
    }

    [SkippableFact]
    public async Task NoMatchReturnsEmpty()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, FullTextSchema.SqlServerDdl, "fts");
        await SetUpFullTextOrSkipAsync(harness);
        await SeedAsync(harness);
        await WaitForPopulationAsync(harness);
        var articles = harness.GetRequiredService<ArticleStore>();

        Assert.Empty(await articles.SearchAsync("nonexistentword"));
    }
}
