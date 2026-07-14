using System.Data;
using Inquiry.SqlServer.Tests.Fixtures;
using Inquiry.Tests.Shared;
using Microsoft.Data.SqlClient;

namespace Inquiry.SqlServer.Tests;

[Collection(SqlServerCollection.Name)]
public sealed class BatchChunkingIntegrationTests
{
    private const string Ddl =
        "CREATE TABLE [BatchChunkItem] ([Id] INT PRIMARY KEY, [Value] NVARCHAR(64) NOT NULL);";

    private readonly SqlServerContainerFixture _fixture;

    public BatchChunkingIntegrationTests(SqlServerContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task FiveItemMutationsUseBoundedChunksAndSqlServerTransports()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        var probe = new BatchExecutionProbe(InspectStructuredParameter);
        await using var harness = await CreateAsync(probe);
        var store = harness.GetRequiredService<BatchChunkItemStore>();

        Assert.Equal(5, await store.InsertAllAsync(Items(5)));
        Assert.Equal(new[] { 2, 2, 1 }, probe.InitializedChunkSizes);

        probe.Reset();
        Assert.Equal(5, await store.UpdateAllAsync(Items(5, valuePrefix: "updated")));
        Assert.Empty(probe.InitializedChunkSizes);
        Assert.Equal(3, probe.CreateBatchCount);
        Assert.Empty(probe.FinalizedCommands);

        probe.Reset();
        Assert.Equal(5, await store.DeleteAllAsync(Enumerable.Range(1, 5)));
        Assert.Equal(new[] { 2, 2, 1 }, probe.InitializedChunkSizes);
        Assert.All(probe.FinalizedCommands, command =>
        {
            Assert.Contains("IN (SELECT [Value] FROM @keys)", command.CommandText, StringComparison.OrdinalIgnoreCase);
            var metadata = Assert.IsType<StructuredParameterMetadata>(command.Metadata);
            Assert.Equal(SqlDbType.Structured, metadata.DbType);
            Assert.StartsWith("[dbo].[Inquiry_Tvp_", metadata.TypeName, StringComparison.Ordinal);
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

        await Assert.ThrowsAsync<SqlException>(() => store.InsertAllAsync(items));

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
    public async Task OneThousandAndOneInsertsSplitAtSqlServerValuesRowBoundary()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        var probe = new BatchExecutionProbe();
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString,
            Ddl,
            "batch_chunk_1001",
            configureServices: probe.Decorate,
            configureOptions: options =>
            {
                options.MaxBatchSize = 2000;
                options.MaxParametersPerCommand = 5000;
            });
        var store = harness.GetRequiredService<BatchChunkItemStore>();

        Assert.Equal(1001, await store.InsertAllAsync(Items(1001)));

        Assert.Equal(new[] { 1000, 1 }, probe.InitializedChunkSizes);
        Assert.Equal(1001, await store.CountAsync());
    }

    private Task<SqlServerTestHarness> CreateAsync(BatchExecutionProbe probe)
        => SqlServerTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString,
            Ddl,
            "batch_chunk",
            configureServices: probe.Decorate,
            configureOptions: options => options.MaxBatchSize = 2);

    private static object? InspectStructuredParameter(System.Data.Common.DbCommand command)
    {
        var parameter = command.Parameters.Cast<SqlParameter>()
            .SingleOrDefault(candidate => candidate.SqlDbType == SqlDbType.Structured);
        return parameter is null ? null : new StructuredParameterMetadata(parameter.SqlDbType, parameter.TypeName);
    }

    private static BatchChunkItem[] Items(int count, string valuePrefix = "value")
        => Enumerable.Range(1, count).Select(id => Item(id, valuePrefix)).ToArray();

    private static BatchChunkItem Item(int id, string valuePrefix = "value")
        => new() { Id = id, Value = valuePrefix + id };

    private sealed record StructuredParameterMetadata(SqlDbType DbType, string TypeName);
}
