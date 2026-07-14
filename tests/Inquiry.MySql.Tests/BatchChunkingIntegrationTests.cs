using System.Data;
using Inquiry.MySql.Tests.Fixtures;
using Inquiry.Tests.Shared;
using MySqlConnector;

namespace Inquiry.MySql.Tests;

[Collection(MySqlCollection.Name)]
public sealed class BatchChunkingIntegrationTests
{
    private const string Ddl =
        "CREATE TABLE `BatchChunkItem` (`Id` INT NOT NULL PRIMARY KEY, `Value` VARCHAR(64) NOT NULL);";

    private readonly MySqlContainerFixture _fixture;

    public BatchChunkingIntegrationTests(MySqlContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task FiveItemMutationsUseBoundedChunksAndMySqlTransports()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        var probe = new BatchExecutionProbe(InspectCommand);
        await using var harness = await CreateAsync(probe);
        var store = harness.GetRequiredService<BatchChunkItemStore>();

        Assert.Equal(5, await store.InsertAllAsync(Items(5)));
        Assert.Equal(new[] { 2, 2, 1 }, probe.InitializedChunkSizes);
        Assert.Equal(0, probe.CreateBatchCount);
        Assert.Equal(new[] { 4, 4, 2 }, probe.FinalizedCommands.Select(ParameterCount));
        Assert.All(probe.FinalizedCommands, command =>
        {
            Assert.Contains("INSERT INTO `BatchChunkItem`", command.CommandText, StringComparison.Ordinal);
            Assert.Contains("VALUES", command.CommandText, StringComparison.Ordinal);
        });

        probe.Reset();
        Assert.Equal(5, await store.UpdateAllAsync(Items(5, valuePrefix: "updated")));
        Assert.Equal(new[] { 2, 2 }, probe.InitializedChunkSizes);
        Assert.Equal(1, probe.CreateBatchCount);
        Assert.Equal(2, probe.FinalizedCommands.Count);
        Assert.All(probe.FinalizedCommands, command =>
        {
            Assert.Contains("UPDATE `BatchChunkItem` AS `_t` INNER JOIN (", command.CommandText, StringComparison.Ordinal);
            Assert.Contains("UNION ALL SELECT", command.CommandText, StringComparison.Ordinal);
            Assert.Contains(") AS `_v` ON", command.CommandText, StringComparison.Ordinal);
            Assert.Contains(" SET `_t`.`Value` = `_v`.`Value`", command.CommandText, StringComparison.Ordinal);
            Assert.Equal(4, ParameterCount(command));
        });

        probe.Reset();
        Assert.Equal(5, await store.DeleteAllAsync(Enumerable.Range(1, 5)));
        Assert.Equal(new[] { 2, 2, 1 }, probe.InitializedChunkSizes);
        Assert.Equal(0, probe.CreateBatchCount);
        Assert.Equal(new[] { "[1,2]", "[3,4]", "[5]" }, probe.FinalizedCommands.Select(JsonValue));
        Assert.All(probe.FinalizedCommands, command =>
        {
            Assert.Contains("JSON_TABLE(@keys, '$[*]' COLUMNS(val INT PATH '$'))", command.CommandText, StringComparison.Ordinal);
            var parameter = Assert.Single(Assert.IsType<CommandMetadata>(command.Metadata).Parameters);
            Assert.Equal(DbType.String, parameter.DbType);
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

        await Assert.ThrowsAsync<MySqlException>(() => store.InsertAllAsync(items));

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

        Assert.Equal(new[] { 2, 2, 1 }, probe.InitializedChunkSizes);
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
    public async Task OneThousandAndOneInsertsSplitAtDefaultParameterBoundary()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        var probe = new BatchExecutionProbe();
        await using var harness = await MySqlTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString,
            Ddl,
            "batch_chunk_1001",
            configureServices: probe.Decorate);
        var store = harness.GetRequiredService<BatchChunkItemStore>();

        Assert.Equal(1001, await store.InsertAllAsync(Items(1001)));

        Assert.Equal(new[] { 1000, 1 }, probe.InitializedChunkSizes);
        Assert.Equal(1001, await store.CountAsync());
    }

    private Task<MySqlTestHarness> CreateAsync(BatchExecutionProbe probe)
        => MySqlTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString,
            Ddl,
            "batch_chunk",
            configureServices: probe.Decorate,
            configureOptions: options => options.MaxBatchSize = 2);

    private static object InspectCommand(System.Data.Common.DbCommand command)
        => new CommandMetadata(command.Parameters.Cast<MySqlParameter>()
            .Select(parameter => new ParameterMetadata(parameter.DbType, parameter.Value))
            .ToArray());

    private static int ParameterCount(FinalizedBatchCommand command)
        => Assert.IsType<CommandMetadata>(command.Metadata).Parameters.Length;

    private static string JsonValue(FinalizedBatchCommand command)
        => Assert.IsType<string>(Assert.Single(Assert.IsType<CommandMetadata>(command.Metadata).Parameters).Value);

    private static BatchChunkItem[] Items(int count, string valuePrefix = "value")
        => Enumerable.Range(1, count).Select(id => Item(id, valuePrefix)).ToArray();

    private static BatchChunkItem Item(int id, string valuePrefix = "value")
        => new() { Id = id, Value = valuePrefix + id };

    private sealed record CommandMetadata(ParameterMetadata[] Parameters);
    private sealed record ParameterMetadata(DbType DbType, object? Value);
}
