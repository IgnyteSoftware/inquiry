using System.Collections.Generic;
using System.Linq;
using Inquiry.FeatureCatalog;
using Inquiry.BulkCopy;
using Inquiry.PostgreSql.Tests.Fixtures;

namespace Inquiry.PostgreSql.Tests;

/// <summary>
/// <c>[InquiryBulkInsert]</c> against real PostgreSQL via the shared <see cref="BulkItem"/> catalog
/// entity: the store method streams rows through Npgsql binary COPY
/// (<see cref="PostgreSqlBulkCopier"/>), returns the written count, and round-trips values
/// including null text and decimals; an empty enumerable writes nothing.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
public sealed class BulkInsertIntegrationTests
{
    private readonly PostgreSqlContainerFixture _fixture;
    public BulkInsertIntegrationTests(PostgreSqlContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task BulkInsertStreamsRowsThroughBinaryCopyAndRoundTrips()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await PostgreSqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, FeatureSchema.PostgreSqlDdl, "bulkins");
        var store = harness.GetRequiredService<BulkItemStore>();

        var items = Enumerable.Range(0, 500)
            .Select(i => new BulkItem
            {
                Category = i % 2 == 0 ? "even" : "odd",
                Amount = 1.25m * i,
                Note = i % 5 == 0 ? null : "note-" + i,
            })
            .ToList();

        var written = await store.BulkInsertAsync(items);

        Assert.Equal(500L, written);
        Assert.Equal(500L, await store.CountAsync());

        var even = await store.ByCategoryAsync("even");
        Assert.Equal(250, even.Count);
        Assert.Contains(even, i => i.Note is null && i.Amount == 0m); // i = 0: null Note round-trips
        Assert.Contains(even, i => i.Note == "note-2" && i.Amount == 2.50m); // decimal round-trips

        var odd = await store.ByCategoryAsync("odd");
        Assert.Equal(250, odd.Count);
        Assert.Contains(odd, i => i.Note == "note-499" && i.Amount == 1.25m * 499);
    }

    [SkippableFact]
    public async Task EmptyBulkInsertWritesNothing()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await PostgreSqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, FeatureSchema.PostgreSqlDdl, "bulkempty");
        var store = harness.GetRequiredService<BulkItemStore>();

        Assert.Equal(0L, await store.BulkInsertAsync(new List<BulkItem>()));
        Assert.Equal(0L, await store.CountAsync());
    }

    [SkippableFact]
    public async Task AmbientBulkInsertRollsBack()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await PostgreSqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, FeatureSchema.PostgreSqlDdl, "bulkrollback");
        var inquiry = harness.GetRequiredService<IInquiry>();
        var store = harness.GetRequiredService<BulkItemStore>();
        await using var transaction = await inquiry.BeginTransactionAsync();

        Assert.Equal(1, await store.BulkInsertAsync(new[] { Item("rollback", 1) }));
        await transaction.RollbackAsync();

        Assert.Equal(0, await store.CountAsync());
    }

    [SkippableFact]
    public async Task AmbientBulkInsertCommits()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await PostgreSqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, FeatureSchema.PostgreSqlDdl, "bulkcommit");
        var inquiry = harness.GetRequiredService<IInquiry>();
        var store = harness.GetRequiredService<BulkItemStore>();
        await using var transaction = await inquiry.BeginTransactionAsync();

        Assert.Equal(1, await store.BulkInsertAsync(new[] { Item("commit", 1) }));
        await transaction.CommitAsync();

        Assert.Equal(1, await store.CountAsync());
    }

    [SkippableFact]
    public async Task CancellationInsideTransactionLeavesNoRows()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await PostgreSqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, FeatureSchema.PostgreSqlDdl, "bulkcancel");
        var inquiry = harness.GetRequiredService<IInquiry>();
        var store = harness.GetRequiredService<BulkItemStore>();
        using var cancellation = new CancellationTokenSource();
        await using var transaction = await inquiry.BeginTransactionAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.BulkInsertAsync(CancelAfter(1000, 50, cancellation), cancellation.Token));
        await transaction.RollbackAsync();

        Assert.Equal(0, await store.CountAsync());
    }

    [SkippableFact]
    public async Task DedicatedBulkInsertsCanRunConcurrently()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await PostgreSqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, FeatureSchema.PostgreSqlDdl, "bulkconcurrent");
        var store = harness.GetRequiredService<BulkItemStore>();

        await Task.WhenAll(
            store.BulkInsertAsync(Enumerable.Range(0, 100).Select(i => Item("left", i))),
            store.BulkInsertAsync(Enumerable.Range(0, 100).Select(i => Item("right", i))));

        Assert.Equal(200, await store.CountAsync());
    }

    [SkippableFact]
    public async Task TimeoutIsSupportedAndOtherProviderOptionsFailBeforeWriting()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await PostgreSqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, FeatureSchema.PostgreSqlDdl, "bulkoptions");
        var store = harness.GetRequiredService<BulkItemStore>();

        Assert.Equal(1, await store.BulkInsertWithOptionsAsync(new[] { Item("timeout", 1) }, new InquiryBulkInsertOptions { Timeout = TimeSpan.FromSeconds(60) }));
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.BulkInsertWithOptionsAsync(new[] { Item("unsupported", 2) }, new InquiryBulkInsertOptions { BatchSize = 10 }));
        Assert.Contains("BatchSize", exception.Message);
        Assert.Equal(1, await store.CountAsync());
    }

    private static BulkItem Item(string category, int value) => new() { Category = category, Amount = value };

    private static IEnumerable<BulkItem> CancelAfter(int count, int cancelAt, CancellationTokenSource cancellation)
    {
        for (var i = 0; i < count; i++)
        {
            if (i == cancelAt) cancellation.Cancel();
            cancellation.Token.ThrowIfCancellationRequested();
            yield return Item("cancel", i);
        }
    }
}
