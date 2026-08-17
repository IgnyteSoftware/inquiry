using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Inquiry.FeatureCatalog;
using Inquiry.BulkCopy;
using Inquiry.Sqlite.Tests.Fixtures;

namespace Inquiry.Sqlite.Tests;

/// <summary>
/// <c>[InquiryBulkInsert]</c> on SQLite — a dialect without a native bulk-copy API — compiles down
/// to the multi-row batch insert. Same store method, same semantics, batch SQL underneath.
/// </summary>
public sealed class BulkInsertFallbackIntegrationTests
{
    [Fact]
    public async Task BulkInsertFallsBackToBatchSqlAndRoundTrips()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(FeatureSchema.SqliteDdl, "BulkFallback");
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
        Assert.Equal(125, even.Count);
        Assert.Contains(even, i => i.Note is null);
        Assert.Contains(even, i => i.Amount == 1.25m * 2);
    }

    [Fact]
    public async Task EmptyBulkInsertIsANoOp()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(FeatureSchema.SqliteDdl, "BulkFallback");
        var store = harness.GetRequiredService<BulkItemStore>();

        Assert.Equal(0L, await store.BulkInsertAsync(new List<BulkItem>()));
        Assert.Equal(0L, await store.CountAsync());
    }

    [Fact]
    public async Task NativeOptionsAreRejectedBeforeFallbackWrites()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(FeatureSchema.SqliteDdl, "BulkFallbackOptions");
        var store = harness.GetRequiredService<BulkItemStore>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.BulkInsertWithOptionsAsync(new[] { new BulkItem { Category = "unsupported" } }, new InquiryBulkInsertOptions { Timeout = TimeSpan.FromSeconds(1) }));

        Assert.Contains("not supported", exception.Message);
        Assert.Equal(0, await store.CountAsync());
    }
}
