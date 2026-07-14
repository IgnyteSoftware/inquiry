using Inquiry.Sqlite.Tests.Fixtures;
using Inquiry.Tests.Shared;
using Microsoft.Data.Sqlite;

namespace Inquiry.Sqlite.Tests;

public sealed class BatchChunkingIntegrationTests
{
    private const string Ddl =
        "CREATE TABLE BatchChunkItem (Id INTEGER PRIMARY KEY, Value TEXT NOT NULL);";

    [Fact]
    public async Task FiveItemMutationsUseBoundedChunksAndSQLiteTransports()
    {
        var probe = new BatchExecutionProbe(InspectJsonParameter);
        await using var harness = await CreateAsync(probe);
        var store = harness.GetRequiredService<BatchChunkItemStore>();

        Assert.Equal(5, await store.InsertAllAsync(Items(5)));
        Assert.Equal(new[] { 2, 2, 1 }, probe.InitializedChunkSizes);

        probe.Reset();
        var updates = new ExecutionBoundaryEnumerable<BatchChunkItem>(
            Items(5, valuePrefix: "updated"),
            () => probe.FinalizedCommands.Count);
        Assert.Equal(5, await store.UpdateAllAsync(updates));
        Assert.Empty(probe.InitializedChunkSizes);
        Assert.Equal(0, probe.CreateBatchCount);
        Assert.Equal(5, probe.FinalizedCommands.Count);
        Assert.Equal(new[] { 0, 0, 2, 2, 4, 4, 5 }, updates.ObservedExecutionCounts);

        probe.Reset();
        Assert.Equal(5, await store.DeleteAllAsync(Enumerable.Range(1, 5)));
        Assert.Equal(new[] { 2, 2, 1 }, probe.InitializedChunkSizes);
        Assert.Equal(3, probe.FinalizedCommands.Count);
        Assert.All(probe.FinalizedCommands, command =>
        {
            Assert.Contains("json_each", command.CommandText, StringComparison.OrdinalIgnoreCase);
            var metadata = Assert.IsType<JsonParameterMetadata>(command.Metadata);
            Assert.Equal(SqliteType.Text, metadata.SqliteType);
            Assert.StartsWith("[", metadata.Value, StringComparison.Ordinal);
        });
        Assert.Equal(0, await store.CountAsync());
    }

    [Fact]
    public async Task DuplicateKeyInLaterChunkRollsBackEarlierChunks()
    {
        var probe = new BatchExecutionProbe();
        await using var harness = await CreateAsync(probe);
        var store = harness.GetRequiredService<BatchChunkItemStore>();
        var items = new[] { Item(1), Item(2), Item(3), Item(2), Item(5) };

        await Assert.ThrowsAsync<SqliteException>(() => store.InsertAllAsync(items));

        Assert.Equal(0, await store.CountAsync());
        Assert.Equal(new[] { 2, 2 }, probe.InitializedChunkSizes);
    }

    [Fact]
    public async Task AmbientTransactionOwnsOutcomeAndOuterRollbackRemovesBatch()
    {
        var probe = new BatchExecutionProbe();
        await using var harness = await CreateAsync(probe);
        var store = harness.GetRequiredService<BatchChunkItemStore>();
        var inquiry = harness.GetRequiredService<IInquiry>();

        await using (await inquiry.BeginTransactionAsync())
        {
            Assert.Equal(5, await store.InsertAllAsync(Items(5)));
            Assert.Equal(5, await store.CountAsync());
        }

        Assert.Equal(0, await store.CountAsync());
    }

    [Fact]
    public async Task CancellationWhileFillingSecondChunkRollsBackAndDisposesSourceOnce()
    {
        var probe = new BatchExecutionProbe();
        await using var harness = await CreateAsync(probe);
        var store = harness.GetRequiredService<BatchChunkItemStore>();
        using var cancellation = new CancellationTokenSource();
        var source = new SinglePassCancellingEnumerable<BatchChunkItem>(Items(5), cancellation, cancelAtMoveNext: 4);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => store.InsertAllAsync(source, cancellation.Token));

        Assert.Equal(0, await store.CountAsync());
        Assert.Equal(1, source.GetEnumeratorCount);
        Assert.Equal(4, source.MoveNextCount);
        Assert.Equal(1, source.DisposeCount);
        Assert.Equal(new[] { 2, 2 }, probe.InitializedChunkSizes);
    }

    [Fact]
    public async Task OneThousandAndOneInsertsSplitAtDefaultMemoryBoundary()
    {
        var probe = new BatchExecutionProbe();
        await using var harness = await SqliteTestHarness.CreateAsync(
            Ddl,
            "batch_chunk_1001",
            configureServices: probe.Decorate);
        var store = harness.GetRequiredService<BatchChunkItemStore>();

        Assert.Equal(1001, await store.InsertAllAsync(Items(1001)));

        Assert.Equal(new[] { 1000, 1 }, probe.InitializedChunkSizes);
        Assert.Equal(1001, await store.CountAsync());
    }

    private static Task<SqliteTestHarness> CreateAsync(BatchExecutionProbe probe)
        => SqliteTestHarness.CreateAsync(
            Ddl,
            "batch_chunk",
            configureOptions: options => options.MaxBatchSize = 2,
            configureServices: probe.Decorate);

    private static object? InspectJsonParameter(System.Data.Common.DbCommand command)
    {
        var parameter = command.Parameters.Cast<SqliteParameter>()
            .SingleOrDefault(candidate => candidate.ParameterName == "@keys");
        return parameter is null
            ? null
            : new JsonParameterMetadata(parameter.SqliteType, Assert.IsType<string>(parameter.Value));
    }

    private static BatchChunkItem[] Items(int count, string valuePrefix = "value")
        => Enumerable.Range(1, count).Select(id => Item(id, valuePrefix)).ToArray();

    private static BatchChunkItem Item(int id, string valuePrefix = "value")
        => new() { Id = id, Value = valuePrefix + id };

    private sealed record JsonParameterMetadata(SqliteType SqliteType, string Value);
}
