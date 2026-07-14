using Inquiry.PostgreSql.Tests.Fixtures;
using Inquiry.Tests.Shared;
using Npgsql;
using NpgsqlTypes;

namespace Inquiry.PostgreSql.Tests;

[Collection(PostgreSqlCollection.Name)]
public sealed class BatchChunkingIntegrationTests
{
    private const string Ddl =
        "CREATE TABLE \"BatchChunkItem\" (\"Id\" INTEGER PRIMARY KEY, \"Value\" TEXT NOT NULL);";

    private readonly PostgreSqlContainerFixture _fixture;

    public BatchChunkingIntegrationTests(PostgreSqlContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task FiveItemMutationsUseBoundedChunksAndPostgreSqlTransports()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        var probe = new BatchExecutionProbe(InspectArrayParameter);
        await using var harness = await CreateAsync(probe);
        var store = harness.GetRequiredService<BatchChunkItemStore>();

        Assert.Equal(5, await store.InsertAllAsync(Items(5)));
        Assert.Equal(new[] { 2, 2, 1 }, probe.InitializedChunkSizes);

        probe.Reset();
        Assert.Equal(5, await store.UpdateAllAsync(Items(5, valuePrefix: "updated")));
        Assert.Empty(probe.InitializedChunkSizes);
        Assert.Equal(3, probe.CreateBatchCount);
        Assert.Equal(new[] { 2, 2, 1 }, probe.ExecutedBatchSizes);
        Assert.Empty(probe.FinalizedCommands);

        probe.Reset();
        Assert.Equal(5, await store.DeleteAllAsync(Enumerable.Range(1, 5)));
        Assert.Equal(new[] { 2, 2, 1 }, probe.InitializedChunkSizes);
        Assert.Equal(3, probe.FinalizedCommands.Count);
        Assert.All(probe.FinalizedCommands, command =>
        {
            Assert.Contains("= ANY(@keys)", command.CommandText, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(
                NpgsqlDbType.Array | NpgsqlDbType.Integer,
                Assert.IsType<NpgsqlDbType>(command.Metadata));
        });
        Assert.Equal(0, await store.CountAsync());
    }

    [SkippableFact]
    public async Task DuplicateKeyInLaterChunkRollsBackEarlierChunks()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        var probe = new BatchExecutionProbe();
        await using var harness = await CreateAsync(probe);
        var store = harness.GetRequiredService<BatchChunkItemStore>();
        var items = new[] { Item(1), Item(2), Item(3), Item(2), Item(5) };

        await Assert.ThrowsAsync<PostgresException>(() => store.InsertAllAsync(items));

        Assert.Equal(0, await store.CountAsync());
        Assert.Equal(new[] { 2, 2 }, probe.InitializedChunkSizes);
    }

    [SkippableFact]
    public async Task AmbientTransactionOwnsOutcomeAndOuterRollbackRemovesBatch()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
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

    [SkippableFact]
    public async Task CancellationWhileFillingSecondChunkRollsBackAndDisposesSourceOnce()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
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

    [SkippableFact]
    public async Task OneThousandAndOneInsertsSplitAtDefaultMemoryBoundary()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        var probe = new BatchExecutionProbe();
        await using var harness = await PostgreSqlTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString,
            Ddl,
            "batch_chunk_1001",
            configureServices: probe.Decorate);
        var store = harness.GetRequiredService<BatchChunkItemStore>();

        Assert.Equal(1001, await store.InsertAllAsync(Items(1001)));

        Assert.Equal(new[] { 1000, 1 }, probe.InitializedChunkSizes);
        Assert.Equal(1001, await store.CountAsync());
    }

    private Task<PostgreSqlTestHarness> CreateAsync(BatchExecutionProbe probe)
        => PostgreSqlTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString,
            Ddl,
            "batch_chunk",
            configureOptions: options => options.MaxBatchSize = 2,
            configureServices: probe.Decorate);

    private static object? InspectArrayParameter(System.Data.Common.DbCommand command)
    {
        var parameter = command.Parameters.Cast<NpgsqlParameter>()
            .SingleOrDefault(candidate => (candidate.NpgsqlDbType & NpgsqlDbType.Array) != 0);
        return parameter?.NpgsqlDbType;
    }

    private static BatchChunkItem[] Items(int count, string valuePrefix = "value")
        => Enumerable.Range(1, count).Select(id => Item(id, valuePrefix)).ToArray();

    private static BatchChunkItem Item(int id, string valuePrefix = "value")
        => new() { Id = id, Value = valuePrefix + id };
}
