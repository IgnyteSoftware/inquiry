using System.Linq;
using Inquiry.Northwind.Models;
using Inquiry.Northwind.Stores;
using Inquiry.Oracle.Tests.Fixtures;

namespace Inquiry.Oracle.Tests;

/// <summary>
/// Batch operations over the Northwind <c>Region</c> entity against real Oracle. Batch <c>InsertAll</c>
/// works via Oracle's set-based <c>INSERT ALL … SELECT 1 FROM dual</c> (a single statement, so the affected
/// row count round-trips), and <c>DeleteAll</c> works via the dialect-aware <c>:keys</c> IN-expansion sentinel
/// (an empty collection rewrites to <c>IN (NULL)</c> — a no-op). <c>UpdateAll</c> executes the single-row
/// UPDATE once per item through the runtime batch API (sequential same-connection fallback on Oracle).
/// </summary>
[Collection(OracleCollection.Name)]
public sealed class BatchDeleteIntegrationTests
{
    private readonly OracleContainerFixture _fixture;
    public BatchDeleteIntegrationTests(OracleContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task DeletesOnlyListedKeys()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "batchdel");
        var regions = harness.GetRequiredService<RegionStore>();

        for (var i = 1; i <= 5; i++)
        {
            await regions.InsertAsync(new Region { RegionID = i, RegionDescription = "R" + i });
        }

        var affected = await regions.DeleteAllAsync(new[] { 1, 3, 5 });

        Assert.Equal(3, affected);
        var remaining = await regions.SelectAllAsync().ToListAsync();
        Assert.Equal(new[] { 2, 4 }, remaining.Select(r => r.RegionID).OrderBy(x => x).ToArray());
    }

    [SkippableFact]
    public async Task EmptyCollectionIsNoOp()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "batchdelempty");
        var regions = harness.GetRequiredService<RegionStore>();

        await regions.InsertAsync(new Region { RegionID = 1, RegionDescription = "Eastern" });

        Assert.Equal(0, await regions.DeleteAllAsync(System.Array.Empty<int>()));
        Assert.Single(await regions.SelectAllAsync().ToListAsync());
    }

    [SkippableFact]
    public async Task InsertAllInsertsEveryRow()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        // Oracle batch insert emits INSERT ALL … SELECT 1 FROM dual — one statement over the whole collection.
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "batchins");
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
    public async Task InsertAllEmptyCollectionIsNoOp()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "batchinsempty");
        var regions = harness.GetRequiredService<RegionStore>();

        Assert.Equal(0, await regions.InsertAllAsync(System.Array.Empty<Region>()));
        Assert.Empty(await regions.SelectAllAsync().ToListAsync());
    }

    [SkippableFact]
    public async Task UpdateAllUpdatesEachRowByKey()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        // UpdateAll executes the ordinary single-row UPDATE once per item through the runtime batch API
        // (sequential same-connection fallback on Oracle), mirroring the other providers' UpdateAll tests.
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "batchupd");
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

        // Empty collection is a no-op.
        Assert.Equal(0, await regions.UpdateAllAsync(System.Array.Empty<Region>()));
    }
}
