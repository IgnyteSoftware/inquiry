using System.Linq;
using Inquiry.Northwind.Models;
using Inquiry.Northwind.Stores;
using Inquiry.Oracle.Tests.Fixtures;

namespace Inquiry.Oracle.Tests;

/// <summary>
/// W3 batch operations over the Northwind <c>Region</c> entity against real Oracle. Batch <c>DeleteAll</c>
/// works: the <c>(keys)</c> IN-expansion sentinel is dialect-aware (Oracle's <c>:</c> sigil), so
/// <c>DELETE … WHERE RegionID IN (…)</c> removes exactly the listed rows and an empty collection rewrites to
/// <c>IN (NULL)</c> (a no-op) — the same runtime <c>InquiryInExpansion</c> path as the predicate <c>IN</c>.
/// Batch <c>InsertAll</c>/<c>UpdateAll</c>, by contrast, are unsupported on Oracle: their multi-row VALUES /
/// multi-statement UPDATE forms raise ORA-00936, so the generator degrades them to throwing stubs (INQ039)
/// at compile time rather than emit invalid SQL.
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
    public async Task InsertAllAndUpdateAllAreUnsupportedOnOracle()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        // InsertAll/UpdateAll degrade to throwing stubs on Oracle (the generated body throws synchronously),
        // documenting the limitation at runtime to match the compile-time INQ039 + generator-emission test.
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "batchunsup");
        var regions = harness.GetRequiredService<RegionStore>();
        var rows = new[] { new Region { RegionID = 1, RegionDescription = "Eastern" } };

        Assert.Throws<System.NotSupportedException>(() => { _ = regions.InsertAllAsync(rows); });
        Assert.Throws<System.NotSupportedException>(() => { _ = regions.UpdateAllAsync(rows); });
    }
}
