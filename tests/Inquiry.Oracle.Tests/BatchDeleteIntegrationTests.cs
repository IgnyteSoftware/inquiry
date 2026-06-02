using System.Linq;
using Inquiry.Northwind.Models;
using Inquiry.Northwind.Stores;
using Inquiry.Oracle.Tests.Fixtures;

namespace Inquiry.Oracle.Tests;

/// <summary>
/// W3b batch delete over a key collection against real Oracle. The <c>(keys)</c> IN-expansion sentinel is
/// dialect-aware (Oracle's <c>:</c> sigil), so <c>DELETE … WHERE RegionID IN (…)</c> removes exactly the
/// listed rows; an empty collection rewrites to <c>IN (NULL)</c> and is a no-op. This exercises the same
/// runtime <c>InquiryInExpansion</c> path as the predicate <c>IN</c>, via the <c>DeleteAll</c> batch site.
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
}
