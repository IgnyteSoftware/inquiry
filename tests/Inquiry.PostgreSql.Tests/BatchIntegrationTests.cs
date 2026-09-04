using System.Linq;
using Inquiry.Northwind.Models;
using Inquiry.Northwind.Stores;
using Inquiry.PostgreSql.Tests.Fixtures;

namespace Inquiry.PostgreSql.Tests;

/// <summary>
/// Batch operations over the Northwind <c>Region</c> entity (client-supplied int key) against real
/// PostgreSQL: <c>InsertAll</c> inserts a collection in one statement, <c>UpdateAll</c> updates each row
/// by key, a predicate delete with <c>Compare.In</c> removes a key set, and each empty collection is a no-op.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
public sealed class BatchIntegrationTests
{
    private readonly PostgreSqlContainerFixture _fixture;
    public BatchIntegrationTests(PostgreSqlContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task InsertAllInsertsEveryRow()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await PostgreSqlTestHarness.CreateAsync(_fixture.AdminConnectionString, "batchins");
        var regions = harness.GetRequiredService<RegionStore>();

        var affected = await regions.InsertAllAsync(new[]
        {
            new Region { RegionID = 1, RegionDescription = "Eastern" },
            new Region { RegionID = 2, RegionDescription = "Western" },
            new Region { RegionID = 3, RegionDescription = "Northern" },
        });

        Assert.Equal(3, affected);
        var all = await regions.SelectAllAsync().ToListAsync();
        Assert.Equal(new[] { 1, 2, 3 }, all.Select(r => r.RegionID).OrderBy(x => x).ToArray());
    }

    [SkippableFact]
    public async Task UpdateAllUpdatesEachRowByKey()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await PostgreSqlTestHarness.CreateAsync(_fixture.AdminConnectionString, "batchupd");
        var regions = harness.GetRequiredService<RegionStore>();
        await regions.InsertAllAsync(new[]
        {
            new Region { RegionID = 1, RegionDescription = "Eastern" },
            new Region { RegionID = 2, RegionDescription = "Western" },
            new Region { RegionID = 3, RegionDescription = "Northern" },
        });

        var affected = await regions.UpdateAllAsync(new[]
        {
            new Region { RegionID = 1, RegionDescription = "East" },
            new Region { RegionID = 3, RegionDescription = "North" },
        });

        Assert.Equal(2, affected);
        Assert.Equal("East", (await regions.SelectByKeyAsync(1))!.RegionDescription);
        Assert.Equal("Western", (await regions.SelectByKeyAsync(2))!.RegionDescription); // untouched
        Assert.Equal("North", (await regions.SelectByKeyAsync(3))!.RegionDescription);
    }

    [SkippableFact]
    public async Task DeleteAllDeletesOnlyListedKeys()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await PostgreSqlTestHarness.CreateAsync(_fixture.AdminConnectionString, "batchdel");
        var regions = harness.GetRequiredService<RegionStore>();
        for (var i = 1; i <= 5; i++)
        {
            await regions.InsertAsync(new Region { RegionID = i, RegionDescription = "R" + i });
        }

        var affected = await regions.DeleteByKeysAsync(new[] { 1, 3, 5 });

        Assert.Equal(3, affected);
        var remaining = await regions.SelectAllAsync().ToListAsync();
        Assert.Equal(new[] { 2, 4 }, remaining.Select(r => r.RegionID).OrderBy(x => x).ToArray());
    }

    [SkippableFact]
    public async Task EmptyCollectionsAreNoOps()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await PostgreSqlTestHarness.CreateAsync(_fixture.AdminConnectionString, "batchempty");
        var regions = harness.GetRequiredService<RegionStore>();
        await regions.InsertAsync(new Region { RegionID = 1, RegionDescription = "Eastern" });

        Assert.Equal(0, await regions.InsertAllAsync(System.Array.Empty<Region>()));
        Assert.Equal(0, await regions.UpdateAllAsync(System.Array.Empty<Region>()));
        Assert.Equal(0, await regions.DeleteByKeysAsync(System.Array.Empty<int>()));
        Assert.Single(await regions.SelectAllAsync().ToListAsync());
    }
}
