using System.Collections.Generic;
using System.Linq;
using Inquiry.FeatureCatalog;
using Inquiry.BulkCopy;
using Inquiry.Oracle.Tests.Fixtures;

namespace Inquiry.Oracle.Tests;

/// <summary>
/// <c>[InquiryBulkInsert]</c> on Oracle — a dialect without a native bulk-copy API — compiles down
/// to the multi-row batch insert (<c>INSERT INTO ... SELECT ... FROM dual UNION ALL</c>).
/// Same store method, same semantics, batch SQL underneath.
/// </summary>
[Collection(OracleCollection.Name)]
public sealed class BulkInsertFallbackIntegrationTests
{
    private readonly OracleContainerFixture _fixture;
    public BulkInsertFallbackIntegrationTests(OracleContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task BulkInsertFallsBackToBatchSqlAndRoundTrips()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString, FeatureSchema.OracleDdl, "bulkins");
        var store = harness.GetRequiredService<BulkItemStore>();

        var items = Enumerable.Range(0, 250)
            .Select(i => new BulkItem
            {
                Category = i % 2 == 0 ? "even" : "odd",
                Amount = 1.25m * i,
                Note = i % 5 == 0 ? null : "note-" + i,
            })
            .ToList();

        var written = await store.BulkInsertAsync(items);

        Assert.Equal(250L, written);
        Assert.Equal(250L, await store.CountAsync());

        var even = await store.ByCategoryAsync("even");
        var odd = await store.ByCategoryAsync("odd");
        Assert.Equal(125, even.Count);
        Assert.Equal(250, even.Concat(odd).Select(static item => item.Id).Distinct().Count());
        Assert.All(even.Concat(odd), static item => Assert.True(item.Id > 0));
        Assert.Contains(even, i => i.Note is null);
        Assert.Contains(even, i => i.Amount == 1.25m * 2);
    }

    [SkippableFact]
    public async Task EmptyBulkInsertIsANoOp()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString, FeatureSchema.OracleDdl, "bulkempty");
        var store = harness.GetRequiredService<BulkItemStore>();

        Assert.Equal(0L, await store.BulkInsertAsync(new List<BulkItem>()));
        Assert.Equal(0L, await store.CountAsync());
    }

    [SkippableFact]
    public async Task NativeOptionsAreRejectedBeforeFallbackWrites()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString, FeatureSchema.OracleDdl, "bulkoptions");
        var store = harness.GetRequiredService<BulkItemStore>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.BulkInsertWithOptionsAsync(new[] { new BulkItem { Category = "unsupported" } }, new InquiryBulkInsertOptions { Timeout = TimeSpan.FromSeconds(1) }));

        Assert.Contains("not supported", exception.Message);
        Assert.Equal(0, await store.CountAsync());
    }
}
