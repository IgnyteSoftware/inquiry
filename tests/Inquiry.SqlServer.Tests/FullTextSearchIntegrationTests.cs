using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Inquiry.FeatureCatalog.FullText;
using Inquiry.IntegrationTesting;
using Inquiry.SqlServer.Tests.Fixtures;
using Microsoft.Data.SqlClient;

namespace Inquiry.SqlServer.Tests;

/// <summary>
/// Full-text search against real SQL Server via the shared <see cref="Article"/> catalog entity. The
/// generated predicate is <c>FREETEXT(([Title],[Body]), @searchTerm)</c>. SQL Server full-text search
/// requires the optional full-text engine component plus a full-text catalog/index with ASYNC population;
/// the default local test container lacks the engine, so local runs skip when it is unavailable. Required
/// provider runs use the pinned FTS image and fail if the capability is absent.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class FullTextSearchIntegrationTests
{
    private const string MissingCapabilityReason =
        "SQL Server full-text component not installed (IsFullTextInstalled != 1).";

    private readonly SqlServerContainerFixture _fixture;
    public FullTextSearchIntegrationTests(SqlServerContainerFixture fixture) => _fixture = fixture;

    private static async Task SeedAsync(SqlServerTestHarness harness)
    {
        var articles = harness.GetRequiredService<ArticleStore>();
        await articles.InsertAsync(new Article { Title = "SQL Server Guide", Body = "An introduction to database systems inquiryreadinesssentinel" });
        await articles.InsertAsync(new Article { Title = "Database Design", Body = "indexes and keys inquiryreadinesssentinel" });
        await articles.InsertAsync(new Article { Title = "Cooking", Body = "recipes and food inquiryreadinesssentinel" });
    }

    private static async Task<bool> SetUpFullTextAsync(
        SqlServerTestHarness harness,
        string catalogSql = FullTextSchema.SqlServerCreateCatalog,
        string indexSql = FullTextSchema.SqlServerCreateIndex)
    {
        await using var connection = new SqlConnection(harness.ConnectionString);
        await connection.OpenAsync();

        await using (var check = connection.CreateCommand())
        {
            check.CommandText = "SELECT FULLTEXTSERVICEPROPERTY('IsFullTextInstalled')";
            var value = await check.ExecuteScalarAsync();
            var isInstalled = value is not null && value != DBNull.Value && Convert.ToInt32(value) == 1;
            if (SqlServerFullTextPolicy.ShouldSkip(DockerRequirement.IsRequired(), isInstalled)) return false;
        }

        await using (var catalog = connection.CreateCommand())
        {
            catalog.CommandText = catalogSql;
            await catalog.ExecuteNonQueryAsync();
        }

        await using (var index = connection.CreateCommand())
        {
            index.CommandText = indexSql;
            await index.ExecuteNonQueryAsync();
        }

        return true;
    }

    private static async Task WaitForPopulationAsync(SqlServerTestHarness harness)
    {
        await using var connection = new SqlConnection(harness.ConnectionString);
        await connection.OpenAsync();
        var articles = harness.GetRequiredService<ArticleStore>();

        var timeout = TimeSpan.FromSeconds(15);
        var elapsed = Stopwatch.StartNew();
        int? lastStatus = null;
        int? lastPendingChanges = null;
        int? lastSentinelCount = null;
        while (elapsed.Elapsed < timeout)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText =
                "SELECT OBJECTPROPERTYEX(OBJECT_ID('Article'),'TableFulltextPopulateStatus'), " +
                "OBJECTPROPERTYEX(OBJECT_ID('Article'),'TableFulltextPendingChanges')";
            await using (var reader = await cmd.ExecuteReaderAsync())
            {
                if (await reader.ReadAsync())
                {
                    lastStatus = reader.IsDBNull(0) ? null : Convert.ToInt32(reader.GetValue(0));
                    lastPendingChanges = reader.IsDBNull(1) ? null : Convert.ToInt32(reader.GetValue(1));
                }
            }

            lastSentinelCount = (await articles.SearchAsync("inquiryreadinesssentinel")).Count;
            if (lastSentinelCount == 3) return;

            await Task.Delay(250);
        }

        throw new TimeoutException(
            $"SQL Server full-text search did not expose the seeded sentinel within {timeout}. " +
            $"Last TableFulltextPopulateStatus: {lastStatus?.ToString() ?? "<null>"}; " +
            $"last TableFulltextPendingChanges: {lastPendingChanges?.ToString() ?? "<null>"}; " +
            $"last sentinel count: {lastSentinelCount?.ToString() ?? "<null>"} (expected 3).");
    }

    [SkippableFact]
    public async Task MatchesRowsContainingTheTerm()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, FullTextSchema.SqlServerDdl, "fts");
        Skip.IfNot(await SetUpFullTextAsync(harness), MissingCapabilityReason);
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
        Skip.IfNot(await SetUpFullTextAsync(harness), MissingCapabilityReason);
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
        Skip.IfNot(await SetUpFullTextAsync(harness), MissingCapabilityReason);
        await SeedAsync(harness);
        await WaitForPopulationAsync(harness);
        var articles = harness.GetRequiredService<ArticleStore>();

        Assert.Empty(await articles.SearchAsync("nonexistentword"));
    }

    [SkippableFact]
    public async Task InvalidIndexSetupPreservesSqlException()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString,
            FullTextSchema.SqlServerDdl,
            "fts_invalid");

        const string invalidIndexSql =
            "CREATE FULLTEXT INDEX ON Article (Title, Body) KEY INDEX PK_Article_Missing WITH CHANGE_TRACKING = AUTO;";

        try
        {
            var configured = await SetUpFullTextAsync(
                harness,
                FullTextSchema.SqlServerCreateCatalog,
                invalidIndexSql);
            Skip.IfNot(configured, MissingCapabilityReason);
            Assert.Fail("Invalid full-text index setup unexpectedly succeeded.");
        }
        catch (SqlException exception)
        {
            Assert.NotEqual(0, exception.Number);
            Assert.Contains("PK_Article_Missing", exception.Message, StringComparison.Ordinal);
        }
    }
}
