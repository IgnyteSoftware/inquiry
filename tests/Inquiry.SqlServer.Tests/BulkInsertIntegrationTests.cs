using System.Linq;
using Inquiry.FeatureCatalog;
using Inquiry.BulkCopy;
using Inquiry.SqlServer.Tests.Fixtures;

namespace Inquiry.SqlServer.Tests;

/// <summary>
/// Bulk insert over the shared <see cref="BulkItem"/> catalog entity against real SQL Server:
/// <c>[InquiryBulkInsert]</c> streams rows through <c>SqlBulkCopy</c>, returns the rows-written
/// count, round-trips decimals and null/non-null strings, and an empty enumerable is a no-op.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class BulkInsertIntegrationTests
{
    private readonly SqlServerContainerFixture _fixture;
    public BulkInsertIntegrationTests(SqlServerContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task BulkInsertStreamsAllRows()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, FeatureSchema.SqlServerDdl, "bulkins");
        var store = harness.GetRequiredService<BulkItemStore>();

        var rows = Enumerable.Range(0, 500).Select(i => new BulkItem
        {
            Category = i % 2 == 0 ? "alpha" : "beta",
            Amount = i + 0.25m,
            Note = i % 3 == 0 ? null : $"note-{i}",
        }).ToArray();

        var written = await store.BulkInsertAsync(rows);

        Assert.Equal(500, written);
        Assert.Equal(500, await store.CountAsync());

        var alphas = await store.ByCategoryAsync("alpha");
        Assert.Equal(250, alphas.Count);
        var betas = await store.ByCategoryAsync("beta");
        Assert.Equal(250, betas.Count);

        var withoutNote = alphas.Single(item => item.Amount == 0.25m); // i = 0: divisible by 3 → null Note
        Assert.Null(withoutNote.Note);

        var withNote = alphas.Single(item => item.Amount == 4.25m); // i = 4
        Assert.Equal("note-4", withNote.Note);
    }

    [SkippableFact]
    public async Task EmptyEnumerableReturnsZero()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, FeatureSchema.SqlServerDdl, "bulkempty");
        var store = harness.GetRequiredService<BulkItemStore>();

        Assert.Equal(0, await store.BulkInsertAsync(Array.Empty<BulkItem>()));
        Assert.Equal(0, await store.CountAsync());
    }

    [SkippableFact]
    public async Task AmbientBulkInsertRollsBack()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, FeatureSchema.SqlServerDdl, "bulkrollback");
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
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, FeatureSchema.SqlServerDdl, "bulkcommit");
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
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, FeatureSchema.SqlServerDdl, "bulkcancel");
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
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, FeatureSchema.SqlServerDdl, "bulkconcurrent");
        var store = harness.GetRequiredService<BulkItemStore>();

        await Task.WhenAll(
            store.BulkInsertAsync(Enumerable.Range(0, 100).Select(i => Item("left", i))),
            store.BulkInsertAsync(Enumerable.Range(0, 100).Select(i => Item("right", i))));

        Assert.Equal(200, await store.CountAsync());
    }

    [SkippableFact]
    public async Task SupportedOptionsAreAppliedAndProgressIsReported()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, FeatureSchema.SqlServerDdl, "bulkoptions");
        var store = harness.GetRequiredService<BulkItemStore>();
        var progress = new List<long>();
        var options = new InquiryBulkInsertOptions
        {
            Timeout = TimeSpan.FromSeconds(60),
            BatchSize = 10,
            TableLock = true,
            NotifyAfter = 10,
            RowsCopied = progress.Add,
        };

        Assert.Equal(25, await store.BulkInsertWithOptionsAsync(Enumerable.Range(0, 25).Select(i => Item("options", i)), options));
        Assert.Contains(10, progress);
        Assert.Contains(20, progress);
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
