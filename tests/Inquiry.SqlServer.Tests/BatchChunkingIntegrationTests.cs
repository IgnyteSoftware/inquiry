using System.Data;
using Inquiry.Commands;
using Inquiry.Interceptors;
using Inquiry.SqlServer.Tests.Fixtures;
using Inquiry.Tests.Shared;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Inquiry.SqlServer.Tests;

[Collection(SqlServerCollection.Name)]
public sealed class BatchChunkingIntegrationTests
{
    private const string Ddl =
        "CREATE TABLE [BatchChunkItem] ([Id] INT PRIMARY KEY, [Value] NVARCHAR(64) NOT NULL);";
    private const string WideDdl =
        "CREATE TABLE [WideBatchChunkItem] ([C0] INT PRIMARY KEY, [C1] INT NOT NULL, [C2] INT NOT NULL, " +
        "[C3] INT NOT NULL, [C4] INT NOT NULL, [C5] INT NOT NULL, [C6] INT NOT NULL, [C7] INT NOT NULL, " +
        "[C8] INT NOT NULL, [C9] INT NOT NULL);";
    private const string DefaultOnlyDdl =
        "CREATE TABLE [DefaultOnlyBatchItem] ([Id] INT IDENTITY(1,1) PRIMARY KEY);";

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
        Assert.Equal(new[] { 2, 2, 1 }, probe.ExecutedBatchSizes);
        Assert.Empty(probe.FinalizedCommands);

        probe.Reset();
        Assert.Equal(5, await store.DeleteAllAsync(Enumerable.Range(1, 5)));
        Assert.Equal(new[] { 2, 2, 1 }, probe.InitializedChunkSizes);
        Assert.Equal(3, probe.FinalizedCommands.Count);
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

        Assert.Equal(new[] { 1 }, probe.InitializedChunkSizes);
        Assert.Equal(1, probe.CreateBatchCount);
        Assert.Equal(new[] { 1000 }, probe.ExecutedBatchSizes);
        Assert.Equal(1001, await store.CountAsync());
    }

    [SkippableFact]
    public async Task AdaptiveInsertUsesSetBasedAt249AndExactGeneratedDbBatchAt250()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        var probe = new BatchExecutionProbe(inspectExecutedBatchCommand: InspectInsertBatchCommand);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString,
            Ddl,
            "batch_adaptive_boundary",
            configureServices: probe.Decorate,
            configureOptions: options => options.MaxBatchSize = 250);
        var store = harness.GetRequiredService<BatchChunkItemStore>();

        Assert.Equal(249, await store.InsertAllAsync(Items(249)));
        Assert.Equal(new[] { 249 }, probe.InitializedChunkSizes);
        Assert.Equal(0, probe.CreateBatchCount);
        Assert.Single(probe.FinalizedCommands);

        probe.Reset();
        Assert.Equal(250, await store.InsertAllAsync(Items(250, firstId: 1000)));
        Assert.Empty(probe.InitializedChunkSizes);
        Assert.Equal(1, probe.CreateBatchCount);
        Assert.Equal(new[] { 250 }, probe.ExecutedBatchSizes);
        var executed = Assert.Single(probe.ExecutedBatches);
        Assert.Equal(250, executed.Commands.Count);
        Assert.All(executed.Commands, command =>
        {
            Assert.Equal("INSERT INTO [BatchChunkItem] ([Id], [Value]) VALUES (@Id, @Value)", command.CommandText);
            var metadata = Assert.IsType<InsertBatchMetadata>(command.Metadata);
            Assert.Equal(new[] { "@Id", "@Value" }, metadata.Names);
            Assert.Equal(new[] { DbType.Int32, DbType.String }, metadata.Types);
        });
        Assert.Equal(499, await store.CountAsync());
    }

    [SkippableFact]
    public async Task WideAdaptiveInsertUsesOneDbBatchBeyondAggregateParameterLimit()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        var probe = new BatchExecutionProbe(inspectExecutedBatchCommand: InspectInsertBatchCommand);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString,
            WideDdl,
            "batch_wide_adaptive",
            configureServices: probe.Decorate,
            configureOptions: options => options.MaxBatchSize = 1000);
        var store = harness.GetRequiredService<WideBatchChunkItemStore>();

        Assert.Equal(250, await store.InsertAllAsync(WideItems(250)));
        Assert.Empty(probe.InitializedChunkSizes);
        Assert.Equal(1, probe.CreateBatchCount);
        Assert.Equal(new[] { 250 }, probe.ExecutedBatchSizes);
        Assert.All(Assert.Single(probe.ExecutedBatches).Commands, command =>
            Assert.Equal(10, Assert.IsType<InsertBatchMetadata>(command.Metadata).Names.Length));

        probe.Reset();
        Assert.Equal(251, await store.InsertAllAsync(WideItems(251, firstId: 1000)));
        Assert.Empty(probe.InitializedChunkSizes);
        Assert.Equal(1, probe.CreateBatchCount);
        Assert.Equal(new[] { 251 }, probe.ExecutedBatchSizes);
        Assert.Equal(501, await store.CountAsync());
    }

    [SkippableFact]
    public async Task DefaultOnlyInsertUsesDbBatchWithoutInterceptors()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        var probe = new BatchExecutionProbe();
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString,
            DefaultOnlyDdl,
            "batch_default_only",
            configureServices: probe.Decorate);
        var store = harness.GetRequiredService<DefaultOnlyBatchItemStore>();

        Assert.Equal(3, await store.InsertAllAsync(DefaultOnlyItems(3)));
        Assert.Equal(1, probe.CreateBatchCount);
        Assert.Equal(new[] { 3 }, probe.ExecutedBatchSizes);
        Assert.Empty(probe.FinalizedCommands);
        Assert.Equal(3, await store.CountAsync());
    }

    [SkippableFact]
    public async Task DefaultOnlyInsertWithActiveInterceptorUsesPerRowLifecycleInsteadOfDbBatch()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        var probe = new BatchExecutionProbe();
        var interceptor = new CountingInterceptor();
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString,
            DefaultOnlyDdl,
            "batch_default_only_intercepted",
            configureServices: services =>
            {
                services.AddSingleton<IInquiryCommandInterceptor>(interceptor);
                probe.Decorate(services);
            });
        var store = harness.GetRequiredService<DefaultOnlyBatchItemStore>();

        Assert.Equal(3, await store.InsertAllAsync(DefaultOnlyItems(3)));
        Assert.Equal(0, probe.CreateBatchCount);
        Assert.Equal(3, probe.FinalizedCommands.Count);
        Assert.All(probe.FinalizedCommands, command =>
            Assert.Equal("INSERT INTO [DefaultOnlyBatchItem] DEFAULT VALUES", command.CommandText));
        Assert.Equal(3, interceptor.InitializedCount);
        Assert.Equal(3, interceptor.ExecutedCount);
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

    private static BatchChunkItem[] Items(int count, string valuePrefix = "value", int firstId = 1)
        => Enumerable.Range(firstId, count).Select(id => Item(id, valuePrefix)).ToArray();

    private static BatchChunkItem Item(int id, string valuePrefix = "value")
        => new() { Id = id, Value = valuePrefix + id };

    private static WideBatchChunkItem[] WideItems(int count, int firstId = 1)
        => Enumerable.Range(firstId, count).Select(static id => new WideBatchChunkItem
        {
            C0 = id,
            C1 = id,
            C2 = id,
            C3 = id,
            C4 = id,
            C5 = id,
            C6 = id,
            C7 = id,
            C8 = id,
            C9 = id,
        }).ToArray();

    private static DefaultOnlyBatchItem[] DefaultOnlyItems(int count)
        => Enumerable.Range(0, count).Select(static _ => new DefaultOnlyBatchItem()).ToArray();

    private static object InspectInsertBatchCommand(System.Data.Common.DbBatchCommand command)
    {
        var parameters = command.Parameters.Cast<SqlParameter>().ToArray();
        return new InsertBatchMetadata(
            parameters.Select(static parameter => parameter.ParameterName).ToArray(),
            parameters.Select(static parameter => parameter.DbType).ToArray());
    }

    private sealed record StructuredParameterMetadata(SqlDbType DbType, string TypeName);
    private sealed record InsertBatchMetadata(string[] Names, DbType[] Types);

    private sealed class CountingInterceptor : IInquiryCommandInterceptor
    {
        internal int InitializedCount { get; private set; }
        internal int ExecutedCount { get; private set; }

        public ValueTask CommandInitializedAsync(
            InquiryCommandContext context,
            CancellationToken cancellationToken = default)
        {
            InitializedCount++;
            return ValueTask.CompletedTask;
        }

        public ValueTask CommandExecutedAsync(
            InquiryCommandExecutedContext context,
            CancellationToken cancellationToken = default)
        {
            ExecutedCount++;
            return ValueTask.CompletedTask;
        }
    }
}
