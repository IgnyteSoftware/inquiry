using System.Data.Common;
using System.Diagnostics;
using Inquiry.BulkCopy;
using Inquiry.Connections;
using Inquiry.Diagnostics;
using Inquiry.FeatureCatalog;
using Inquiry.MySql.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;

namespace Inquiry.MySql.Tests;

/// <summary>
/// <c>[InquiryBulkInsert]</c> against real MySQL via the shared <see cref="BulkItem"/> catalog entity: the
/// generated store method streams rows through MySqlConnector's <c>MySqlBulkCopy</c> (LOAD DATA LOCAL
/// INFILE) using the provider-registered <c>IInquiryBulkCopier</c>, mapping columns by name so the
/// omitted AUTO_INCREMENT key stays server-generated.
/// </summary>
[Collection(MySqlCollection.Name)]
public sealed class BulkInsertIntegrationTests
{
    private readonly MySqlContainerFixture _fixture;
    public BulkInsertIntegrationTests(MySqlContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task BulkInsertStreamsAllRows()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, FeatureSchema.MySqlDdl, "bulk");
        var store = harness.GetRequiredService<BulkItemStore>();

        var items = Enumerable.Range(1, 500).Select(i => new BulkItem
        {
            Category = i % 2 == 0 ? "even" : "odd",
            Amount = i + 0.25m,
            Note = i % 3 == 0 ? null : $"note {i}",
        }).ToList();

        var inserted = await store.BulkInsertAsync(items);

        Assert.Equal(500, inserted);
        Assert.Equal(500, await store.CountAsync());

        var even = await store.ByCategoryAsync("even");
        Assert.Equal(250, even.Count);
        var sample = Assert.Single(even, x => x.Amount == 2.25m);
        Assert.Equal("note 2", sample.Note);
        Assert.Contains(even, x => x.Note is null);
    }

    [SkippableFact]
    public async Task BulkInsertOfEmptySequenceReturnsZero()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, FeatureSchema.MySqlDdl, "bulk");
        var store = harness.GetRequiredService<BulkItemStore>();

        var inserted = await store.BulkInsertAsync(Enumerable.Empty<BulkItem>());

        Assert.Equal(0, inserted);
        Assert.Equal(0, await store.CountAsync());
    }

    [SkippableFact]
    public async Task AmbientBulkInsertFailsBeforeWritingBecauseRegularConnectionDisablesLocalInfile()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, FeatureSchema.MySqlDdl, "bulktx");
        var inquiry = harness.GetRequiredService<IInquiry>();
        var store = harness.GetRequiredService<BulkItemStore>();
        await using var transaction = await inquiry.BeginTransactionAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.BulkInsertAsync(new[] { Item("transaction", 1) }));

        Assert.Contains("AllowLoadLocalInfile", exception.Message);
        Assert.Contains("[InquiryInsert]", exception.Message);
        Assert.Equal(0, await store.CountAsync());
        await transaction.RollbackAsync();
    }

    [SkippableFact]
    public async Task CancellationStopsStreaming()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, FeatureSchema.MySqlDdl, "bulkcancel");
        var store = harness.GetRequiredService<BulkItemStore>();
        using var cancellation = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.BulkInsertAsync(CancelAfter(1000, 50, cancellation), cancellation.Token));
    }

    [SkippableFact]
    public async Task DedicatedBulkInsertsCanRunConcurrently()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, FeatureSchema.MySqlDdl, "bulkconcurrent");
        var store = harness.GetRequiredService<BulkItemStore>();

        await Task.WhenAll(
            store.BulkInsertAsync(Enumerable.Range(0, 100).Select(i => Item("left", i))),
            store.BulkInsertAsync(Enumerable.Range(0, 100).Select(i => Item("right", i))));

        Assert.Equal(200, await store.CountAsync());
    }

    [SkippableFact]
    public async Task TimeoutAndProgressAreSupportedWhileBatchSizeFailsBeforeWriting()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, FeatureSchema.MySqlDdl, "bulkoptions");
        var store = harness.GetRequiredService<BulkItemStore>();
        var progress = new List<long>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.BulkInsertWithOptionsAsync(new[] { Item("unsupported", 1) }, new InquiryBulkInsertOptions { BatchSize = 10 }));
        Assert.Contains("BatchSize", exception.Message);
        Assert.Equal(25, await store.BulkInsertWithOptionsAsync(
            Enumerable.Range(0, 25).Select(i => Item("supported", i)),
            new InquiryBulkInsertOptions { Timeout = TimeSpan.FromSeconds(60), NotifyAfter = 10, RowsCopied = progress.Add }));
        Assert.Contains(10, progress);
        Assert.Contains(20, progress);
        Assert.Equal(25, await store.CountAsync());
    }

    [SkippableFact]
    public async Task DedicatedWarningReportsCommittedRows()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        var activities = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = static source => source.Name == InquiryTelemetry.ActivitySourceName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => { lock (activities) { activities.Add(activity); } },
        };
        ActivitySource.AddActivityListener(listener);
        await using var harness = await CreatePermissiveHarnessAsync("bulkwarn");
        var store = harness.GetRequiredService<BulkItemStore>();

        var exception = await Assert.ThrowsAsync<InquiryBulkInsertWarningException>(() => store.BulkInsertAsync(RowsWithOverlongCategory()));

        Assert.Equal(3, exception.RowsWritten);
        Assert.False(exception.IsEnlisted);
        Assert.NotEmpty(exception.Warnings);
        Assert.Equal(3, await store.CountAsync());
        Assert.Single(await store.ByCategoryAsync(new string('x', 100)));

        Activity span;
        lock (activities)
        {
            span = Assert.Single(activities, a => a.DisplayName == "BULK_INSERT"
                && (string?)a.GetTagItem("error.type") == typeof(InquiryBulkInsertWarningException).FullName);
        }

        Assert.Equal(3L, span.GetTagItem("db.response.affected_rows"));
        var copy = Assert.Single(span.Events, e => e.Name == "copy.completed");
        Assert.Contains(copy.Tags, tag => tag.Key == "db.response.affected_rows" && Equals(tag.Value, 3L));
    }

    [SkippableFact]
    public async Task EnlistedWarningRowsRollBackWithCallerTransaction()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await CreatePermissiveHarnessAsync("bulkwarntx");
        var inquiry = harness.GetRequiredService<IInquiry>();
        var store = harness.GetRequiredService<BulkItemStore>();

        await using (var transaction = await inquiry.BeginTransactionAsync())
        {
            var exception = await Assert.ThrowsAsync<InquiryBulkInsertWarningException>(() => store.BulkInsertAsync(RowsWithOverlongCategory()));

            Assert.Equal(3, exception.RowsWritten);
            Assert.True(exception.IsEnlisted);
            await transaction.RollbackAsync();
        }

        Assert.Equal(0, await store.CountAsync());
    }

    // A caller-supplied factory that enables LOAD DATA LOCAL INFILE on every connection, so bulk insert
    // can enlist, and selects a permissive sql_mode, so overlong values truncate with a warning.
    private async Task<MySqlTestHarness> CreatePermissiveHarnessAsync(string prefix)
    {
        string? connectionString = null;
        var harness = await MySqlTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString,
            FeatureSchema.MySqlDdl,
            prefix,
            configureServices: services => services.AddSingleton<IInquiryConnectionFactory>(
                _ => new PermissiveConnectionFactory(connectionString!)));
        connectionString = new MySqlConnectionStringBuilder(harness.ConnectionString) { AllowLoadLocalInfile = true }.ToString();
        return harness;
    }

    private static IEnumerable<BulkItem> RowsWithOverlongCategory()
        => new[] { Item("short", 1), Item(new string('x', 150), 2), Item("short", 3) };

    private sealed class PermissiveConnectionFactory: IInquiryConnectionFactory
    {
        private readonly string _connectionString;

        public PermissiveConnectionFactory(string connectionString) => _connectionString = connectionString;

        public async ValueTask<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
        {
            var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SET SESSION sql_mode = ''";
            await command.ExecuteNonQueryAsync(cancellationToken);
            return connection;
        }
    }

    private static BulkItem Item(string category, int value) => new() { Category = category, Amount = value };

    private static IEnumerable<BulkItem> CancelAfter(int count, int cancelAt, CancellationTokenSource cancellation)
    {
        for (var i = 0; i < count; i++)
        {
            if (i == cancelAt)
            {
                cancellation.Cancel();
            }

            cancellation.Token.ThrowIfCancellationRequested();
            yield return Item("cancel", i);
        }
    }
}
