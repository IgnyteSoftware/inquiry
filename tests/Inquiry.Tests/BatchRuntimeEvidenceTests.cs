using Inquiry.Commands;
using Inquiry.Connections;
using Inquiry.Interceptors;
using Inquiry.Pipeline;
using System.Collections;
using System.Data;
using System.Data.Common;

namespace Inquiry.Tests;

public sealed class BatchRuntimeEvidenceTests
{
    [Fact]
    public async Task DbBatchExecutesBoundedChunksAndDisposesEveryPhysicalBatch()
    {
        var state = new RecordingState();
        var pipeline = CreatePipeline(state, InquiryBatchExecutionMode.DbBatch, maxBatchSize: 2);

        var affected = await pipeline.ExecuteBatchAsync(RowCommand(), Enumerable.Range(1, 5));

        Assert.Equal(5, affected);
        Assert.Equal(new[] { 2, 2, 1 }, state.ExecutedBatchSizes);
        Assert.Equal(3, state.BatchCreateCount);
        Assert.Equal(3, state.BatchDisposeCount);
        Assert.Equal(0, state.CommandExecuteCount);
        Assert.Equal(1, state.TransactionCommitCount);
    }

    [Fact]
    public async Task DbBatchWithoutParameterSupportFallsBackToOneReusedCommand()
    {
        var state = new RecordingState { BatchCommandsCanCreateParameters = false };
        var pipeline = CreatePipeline(state, InquiryBatchExecutionMode.DbBatch, maxBatchSize: 2);

        var affected = await pipeline.ExecuteBatchAsync(RowCommand(), Enumerable.Range(1, 5));

        Assert.Equal(5, affected);
        Assert.Empty(state.ExecutedBatchSizes);
        Assert.Equal(1, state.BatchCreateCount);
        Assert.Equal(1, state.BatchDisposeCount);
        Assert.Equal(1, state.CommandCreateCount);
        Assert.Equal(5, state.CommandExecuteCount);
    }

    [Fact]
    public async Task ReusedCommandIsPreparedAtMostOnceAcrossAllChunks()
    {
        var state = new RecordingState();
        var pipeline = CreatePipeline(state, InquiryBatchExecutionMode.ReusedCommand, maxBatchSize: 2, prepare: true);

        await pipeline.ExecuteBatchAsync(RowCommand(), Enumerable.Range(1, 5));

        Assert.Equal(1, state.CommandCreateCount);
        Assert.Equal(1, state.CommandPrepareCount);
        Assert.Equal(5, state.CommandExecuteCount);
    }

    [Fact]
    public async Task SelectableRowsWithActiveInterceptorNeverUseDbBatchAndPreservePerItemLifecycle()
    {
        var state = new RecordingState();
        var interceptor = new LifecycleRecordingInterceptor();
        var factory = new RecordingFactory(state, InquiryBatchExecutionMode.DbBatch);
        var pipeline = new InquiryRequestPipeline(
            factory,
            new IInquiryCommandInterceptor[] { interceptor },
            new InquiryOptions { MaxBatchSize = 2 });

        var affected = await pipeline.ExecuteBatchAsync(SelectableRowCommand(), new[] { 1, 2, 3 });

        Assert.Equal(3, affected);
        Assert.Equal(0, state.BatchCreateCount);
        Assert.Empty(state.ExecutedBatchSizes);
        Assert.Equal(3, state.CommandCreateCount);
        Assert.Equal(3, state.CommandExecuteCount);
        Assert.Equal(3, state.CommandDisposeCount);
        Assert.Equal(
            new[]
            {
                "initialized:1", "executing:1", "executed:1:1",
                "initialized:2", "executing:2", "executed:2:1",
                "initialized:3", "executing:3", "executed:3:1",
            },
            interceptor.Events);
        Assert.Equal(1, state.TransactionCommitCount);
        Assert.Equal(0, state.TransactionRollbackCount);
    }

    [Fact]
    public async Task SelectableRowsWithActiveInterceptorCancellationNeverUsesDbBatchAndRollsBack()
    {
        using var cancellation = new CancellationTokenSource();
        var state = new RecordingState();
        var interceptor = new LifecycleRecordingInterceptor(cancellation, cancelAtValue: 2);
        var factory = new RecordingFactory(state, InquiryBatchExecutionMode.DbBatch);
        var pipeline = new InquiryRequestPipeline(
            factory,
            new IInquiryCommandInterceptor[] { interceptor },
            new InquiryOptions { MaxBatchSize = 3 });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => pipeline.ExecuteBatchAsync(SelectableRowCommand(), new[] { 1, 2, 3 }, cancellation.Token));

        Assert.Equal(0, state.BatchCreateCount);
        Assert.Empty(state.ExecutedBatchSizes);
        Assert.Equal(2, state.CommandCreateCount);
        Assert.Equal(1, state.CommandExecuteCount);
        Assert.Equal(2, state.CommandDisposeCount);
        Assert.Equal(
            new[]
            {
                "initialized:1", "executing:1", "executed:1:1",
                "initialized:2", "executing:2", "failed:2:OperationCanceledException",
            },
            interceptor.Events);
        Assert.Equal(0, state.TransactionCommitCount);
        Assert.Equal(1, state.TransactionRollbackCount);
    }

    [Theory]
    [InlineData(PreparedStatementMode.Auto, false, 1)]
    [InlineData(PreparedStatementMode.None, false, 0)]
    [InlineData(PreparedStatementMode.Auto, true, 1)]
    [InlineData(PreparedStatementMode.None, true, 0)]
    public async Task DescriptorPreferredPreparationHonorsModeAndTransactionOwnership(
        PreparedStatementMode mode,
        bool transacted,
        int expectedPrepareCount)
    {
        var state = new RecordingState();
        var factory = new RecordingFactory(state, InquiryBatchExecutionMode.ReusedCommand);
        var options = new InquiryOptions { MaxBatchSize = 2, PrepareStatements = mode };
        var command = PreferredRowCommand();

        if (transacted)
        {
            var connection = new RecordingConnection(state);
            var transaction = new RecordingTransaction(connection, state);
            var pipeline = new TransactedInquiryRequestPipeline(
                connection, transaction, Array.Empty<IInquiryCommandInterceptor>(), factory, options);

            Assert.Equal(5, await pipeline.ExecuteBatchAsync(command, Enumerable.Range(1, 5)));
        }
        else
        {
            var pipeline = new InquiryRequestPipeline(
                factory, Array.Empty<IInquiryCommandInterceptor>(), options);

            Assert.Equal(5, await pipeline.ExecuteBatchAsync(command, Enumerable.Range(1, 5)));
        }

        Assert.Equal(1, state.CommandCreateCount);
        Assert.Equal(1, state.CommandDisposeCount);
        Assert.Equal(1, state.ParameterCreateCount);
        Assert.Equal(expectedPrepareCount, state.CommandPrepareCount);
        Assert.Equal(5, state.CommandExecuteCount);
        Assert.Equal(transacted ? 0 : 1, state.TransactionCommitCount);
        Assert.Equal(0, state.TransactionRollbackCount);
    }

    [Theory]
    [InlineData(InquiryBatchExecutionMode.DbBatch)]
    [InlineData(InquiryBatchExecutionMode.ArrayBinding)]
    public async Task DescriptorPreparationHintDoesNotLeakIntoPhysicalBatchOrChunkPreparation(
        InquiryBatchExecutionMode mode)
    {
        var state = new RecordingState();
        var factory = new RecordingFactory(state, mode);
        var pipeline = new InquiryRequestPipeline(
            factory,
            Array.Empty<IInquiryCommandInterceptor>(),
            new InquiryOptions { MaxBatchSize = 2, PrepareStatements = PreparedStatementMode.Auto });
        var command = mode == InquiryBatchExecutionMode.DbBatch
            ? PreferredRowCommand()
            : PreferredChunkCapableCommand();

        Assert.Equal(5, await pipeline.ExecuteBatchAsync(command, Enumerable.Range(1, 5)));
        Assert.Equal(0, state.CommandPrepareCount);
        Assert.Equal(0, state.BatchPrepareCount);
        Assert.Equal(mode == InquiryBatchExecutionMode.DbBatch ? 3 : 0, state.BatchCreateCount);
        Assert.Equal(mode == InquiryBatchExecutionMode.ArrayBinding ? 3 : 0, state.CommandCreateCount);
    }

    [Theory]
    [InlineData(PreparedStatementMode.Auto, 1)]
    [InlineData(PreparedStatementMode.None, 0)]
    public async Task UnsupportedDbBatchFallbackHonorsDescriptorPreparationPreference(
        PreparedStatementMode mode,
        int expectedPrepareCount)
    {
        var state = new RecordingState { BatchCommandsCanCreateParameters = false };
        var factory = new RecordingFactory(state, InquiryBatchExecutionMode.DbBatch);
        var pipeline = new InquiryRequestPipeline(
            factory,
            Array.Empty<IInquiryCommandInterceptor>(),
            new InquiryOptions { MaxBatchSize = 2, PrepareStatements = mode });

        Assert.Equal(5, await pipeline.ExecuteBatchAsync(PreferredRowCommand(), Enumerable.Range(1, 5)));
        Assert.Equal(1, state.BatchCreateCount);
        Assert.Equal(1, state.BatchDisposeCount);
        Assert.Equal(1, state.CommandCreateCount);
        Assert.Equal(expectedPrepareCount, state.CommandPrepareCount);
        Assert.Equal(5, state.CommandExecuteCount);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task DescriptorPreparationFailureDisposesCommandAndRespectsTransactionOwnership(
        bool transacted,
        bool cancel)
    {
        var state = new RecordingState
        {
            ThrowOnCommandPrepare = !cancel,
            CancelOnCommandPrepare = cancel,
        };
        var factory = new RecordingFactory(state, InquiryBatchExecutionMode.ReusedCommand);
        var options = new InquiryOptions { PrepareStatements = PreparedStatementMode.Auto };
        Func<Task> execute;

        if (transacted)
        {
            var connection = new RecordingConnection(state);
            var transaction = new RecordingTransaction(connection, state);
            var pipeline = new TransactedInquiryRequestPipeline(
                connection, transaction, Array.Empty<IInquiryCommandInterceptor>(), factory, options);
            execute = () => pipeline.ExecuteBatchAsync(PreferredRowCommand(), new[] { 1 });
        }
        else
        {
            var pipeline = new InquiryRequestPipeline(
                factory, Array.Empty<IInquiryCommandInterceptor>(), options);
            execute = () => pipeline.ExecuteBatchAsync(PreferredRowCommand(), new[] { 1 });
        }

        if (cancel)
            await Assert.ThrowsAnyAsync<OperationCanceledException>(execute);
        else
            await Assert.ThrowsAsync<InvalidOperationException>(execute);

        Assert.Equal(1, state.CommandCreateCount);
        Assert.Equal(1, state.CommandPrepareCount);
        Assert.Equal(1, state.CommandDisposeCount);
        Assert.Equal(0, state.CommandExecuteCount);
        Assert.Equal(0, state.TransactionCommitCount);
        Assert.Equal(transacted ? 0 : 1, state.TransactionRollbackCount);
    }

    [Fact]
    public async Task WholeChunkAndDbBatchPrepareOncePerPhysicalChunk()
    {
        var chunkState = new RecordingState();
        var chunkPipeline = CreatePipeline(
            chunkState, InquiryBatchExecutionMode.ReusedCommand, maxBatchSize: 2, prepare: true);
        var chunkCommand = new InquiryBatchCommand<int>(
            count => "work-" + count,
            (command, items) => ((RecordingCommand)command).AffectedRows = items.Count,
            parametersPerItem: 1);

        Assert.Equal(5, await chunkPipeline.ExecuteBatchAsync(chunkCommand, Enumerable.Range(1, 5)));
        Assert.Equal(3, chunkState.CommandPrepareCount);
        Assert.Equal(3, chunkState.CommandDisposeCount);

        var batchState = new RecordingState();
        var batchPipeline = CreatePipeline(
            batchState, InquiryBatchExecutionMode.DbBatch, maxBatchSize: 2, prepare: true);

        Assert.Equal(5, await batchPipeline.ExecuteBatchAsync(RowCommand(), Enumerable.Range(1, 5)));
        Assert.Equal(3, batchState.BatchPrepareCount);
        Assert.Equal(3, batchState.BatchDisposeCount);
    }

    [Theory]
    [InlineData(InquiryBatchExecutionMode.ReusedCommand, false)]
    [InlineData(InquiryBatchExecutionMode.ReusedCommand, true)]
    [InlineData(InquiryBatchExecutionMode.DbBatch, false)]
    [InlineData(InquiryBatchExecutionMode.DbBatch, true)]
    public async Task TransactedBatchEnlistsOuterTransactionWithoutOwningItsOutcome(
        InquiryBatchExecutionMode mode,
        bool throwOnExecute)
    {
        var state = new RecordingState
        {
            ThrowOnCommandExecute = throwOnExecute,
            ThrowOnBatchExecute = throwOnExecute,
        };
        var connection = new RecordingConnection(state);
        var transaction = new RecordingTransaction(connection, state);
        var factory = new RecordingFactory(state, mode);
        var pipeline = new TransactedInquiryRequestPipeline(
            connection, transaction, Array.Empty<IInquiryCommandInterceptor>(), factory, options: null);

        if (throwOnExecute)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => pipeline.ExecuteBatchAsync(RowCommand(), new[] { 1, 2, 3 }));
        }
        else
        {
            Assert.Equal(3, await pipeline.ExecuteBatchAsync(RowCommand(), new[] { 1, 2, 3 }));
        }

        var assignedTransactions = mode == InquiryBatchExecutionMode.DbBatch
            ? state.BatchTransactions
            : state.CommandTransactions;
        Assert.NotEmpty(assignedTransactions);
        Assert.All(assignedTransactions, assigned => Assert.Same(transaction, assigned));

        Assert.Equal(0, state.TransactionCommitCount);
        Assert.Equal(0, state.TransactionRollbackCount);
        Assert.Equal(0, state.SavepointCreateCount);
        Assert.Equal(0, state.SavepointRollbackCount);
        Assert.Equal(0, state.SavepointReleaseCount);
    }

    [Fact]
    public async Task EffectiveLimitSplitsOneThousandAndOneItemsAtExactBoundary()
    {
        var state = new RecordingState();
        var pipeline = CreatePipeline(
            state, InquiryBatchExecutionMode.ReusedCommand, maxBatchSize: 2000, maxParametersPerCommand: 10000);
        var command = new InquiryBatchCommand<int>(
            count => "work-" + count,
            (dbCommand, items) => ((RecordingCommand)dbCommand).AffectedRows = items.Count,
            parametersPerItem: 2,
            maxItemsPerCommand: 1000);

        Assert.Equal(1001, await pipeline.ExecuteBatchAsync(command, Enumerable.Range(1, 1001)));
        Assert.Equal(new[] { 1000, 1 }, state.InitializedChunkSizes);
    }

    [Theory]
    [InlineData(200, true)]
    [InlineData(201, false)]
    [InlineData(1000, false)]
    public async Task AdaptiveCommandAppliesAggregateParameterCapOnlyToSetBasedBranch(
        int count,
        bool expectedSetBased)
    {
        var state = new RecordingState();
        var pipeline = CreatePipeline(
            state, InquiryBatchExecutionMode.DbBatch, maxBatchSize: 1000,
            maxParametersPerCommand: InquiryOptions.DefaultMaxParametersPerCommand);

        Assert.Equal(count, await pipeline.ExecuteBatchAsync(AdaptiveTenParameterCommand(), Enumerable.Range(1, count)));
        Assert.Equal(expectedSetBased ? new[] { count } : Array.Empty<int>(), state.InitializedChunkSizes);
        Assert.Equal(expectedSetBased ? 0 : 1, state.BatchCreateCount);
        Assert.Equal(expectedSetBased ? Array.Empty<int>() : new[] { count }, state.ExecutedBatchSizes);
    }

    [Fact]
    public async Task AdaptiveCommandUsesDbBatchThenSetBasedTailAtProviderRowBoundary()
    {
        var state = new RecordingState();
        var pipeline = CreatePipeline(
            state, InquiryBatchExecutionMode.DbBatch, maxBatchSize: 2000,
            maxParametersPerCommand: InquiryOptions.DefaultMaxParametersPerCommand);

        Assert.Equal(1001, await pipeline.ExecuteBatchAsync(
            AdaptiveTenParameterCommand(), Enumerable.Range(1, 1001)));
        Assert.Equal(new[] { 1 }, state.InitializedChunkSizes);
        Assert.Equal(1, state.BatchCreateCount);
        Assert.Equal(new[] { 1000 }, state.ExecutedBatchSizes);
    }

    [Theory]
    [InlineData(5, true)]
    [InlineData(6, false)]
    public async Task AdaptiveCommandHonorsConfiguredSetBasedParameterLimit(int count, bool expectedSetBased)
    {
        var state = new RecordingState();
        var pipeline = CreatePipeline(
            state, InquiryBatchExecutionMode.DbBatch, maxBatchSize: 1000, maxParametersPerCommand: 50);

        Assert.Equal(count, await pipeline.ExecuteBatchAsync(AdaptiveTenParameterCommand(), Enumerable.Range(1, count)));
        Assert.Equal(expectedSetBased ? new[] { count } : Array.Empty<int>(), state.InitializedChunkSizes);
        Assert.Equal(expectedSetBased ? 0 : 1, state.BatchCreateCount);
    }

    [Fact]
    public async Task AdaptiveCommandRejectsOneRowThatExceedsConfiguredParameterLimit()
    {
        var state = new RecordingState();
        var pipeline = CreatePipeline(
            state, InquiryBatchExecutionMode.DbBatch, maxBatchSize: 1000, maxParametersPerCommand: 9);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => pipeline.ExecuteBatchAsync(AdaptiveTenParameterCommand(), new[] { 1 }));
        Assert.Equal(0, state.CommandCreateCount);
        Assert.Equal(0, state.BatchCreateCount);
    }

    [Fact]
    public async Task AdaptiveDbBatchBranchFallsBackToOneReusedCommandWhenProviderCannotCreateBatch()
    {
        var state = new RecordingState { CanCreateBatch = false };
        var pipeline = CreatePipeline(
            state, InquiryBatchExecutionMode.DbBatch, maxBatchSize: 1000,
            maxParametersPerCommand: InquiryOptions.DefaultMaxParametersPerCommand);

        Assert.Equal(201, await pipeline.ExecuteBatchAsync(
            AdaptiveTenParameterCommand(), Enumerable.Range(1, 201)));
        Assert.Empty(state.InitializedChunkSizes);
        Assert.Equal(0, state.BatchCreateCount);
        Assert.Equal(1, state.CommandCreateCount);
        Assert.Equal(201, state.CommandExecuteCount);
    }

    [Theory]
    [InlineData(1000, 2100, 2, 1000)]
    [InlineData(1000, 2000, 3, 666)]
    [InlineData(1000, 32766, 40, 819)]
    [InlineData(1000, 65535, 100, 655)]
    [InlineData(2000, 10000, 2, 1000)]
    public void EffectiveLimitHonorsRowAndProviderParameterCaps(
        int maxBatchSize,
        int maxParameters,
        int parametersPerItem,
        int expected)
    {
        var command = new InquiryBatchCommand<int>(
            static _ => "work", static (_, _) => { }, parametersPerItem, maxItemsPerCommand: 1000);

        Assert.Equal(expected, command.GetEffectiveChunkSize(maxBatchSize, maxParameters));
    }

    [Fact]
    public async Task ExecutionAndCleanupFailuresRemainOrderedAcrossAllOwnedResources()
    {
        var state = new RecordingState
        {
            ThrowOnCommandExecute = true,
            ThrowOnCommandDispose = true,
            ThrowOnRollback = true,
            ThrowOnTransactionDispose = true,
            ThrowOnConnectionDispose = true,
        };
        var pipeline = CreatePipeline(state, InquiryBatchExecutionMode.ReusedCommand, maxBatchSize: 2);
        var source = new ThrowingDisposeEnumerable(new[] { 1 });

        var exception = await Assert.ThrowsAsync<AggregateException>(
            () => pipeline.ExecuteBatchAsync(RowCommand(), source));

        var commandFailure = Assert.IsType<AggregateException>(exception.InnerExceptions[0]);
        Assert.Collection(
            commandFailure.InnerExceptions,
            item => Assert.Equal("command execute failed", item.Message),
            item => Assert.Equal("command dispose failed", item.Message));
        Assert.Collection(
            exception.InnerExceptions.Skip(1),
            item => Assert.Equal("enumerator dispose failed", item.Message),
            item => Assert.Equal("transaction rollback failed", item.Message),
            item => Assert.Equal("transaction dispose failed", item.Message),
            item => Assert.Equal("connection dispose failed", item.Message));
        Assert.Equal(
            new[] { "command-dispose", "transaction-rollback", "transaction-dispose", "connection-dispose" },
            state.CleanupEvents);
    }

    private static InquiryRequestPipeline CreatePipeline(
        RecordingState state,
        InquiryBatchExecutionMode mode,
        int maxBatchSize,
        bool prepare = false,
        int maxParametersPerCommand = InquiryOptions.DefaultMaxParametersPerCommand)
    {
        var factory = new RecordingFactory(state, mode, prepare);
        return new InquiryRequestPipeline(
            factory,
            Array.Empty<IInquiryCommandInterceptor>(),
            new InquiryOptions
            {
                MaxBatchSize = maxBatchSize,
                MaxParametersPerCommand = maxParametersPerCommand,
                PrepareStatements = prepare ? PreparedStatementMode.Auto : PreparedStatementMode.None,
            });
    }

    private static InquiryBatchCommand<int> RowCommand()
        => new("work", static (target, item) =>
        {
            var parameter = target.CreateParameter();
            parameter.ParameterName = "value";
            parameter.Value = item;
            target.AddParameter(parameter);
        });

    private static InquiryBatchCommand<int> PreferredRowCommand()
        => new(
            "work",
            static (target, item) =>
            {
                var parameter = target.CreateParameter();
                parameter.ParameterName = "value";
                parameter.Value = item;
                target.AddParameter(parameter);
            },
            CommandType.Text,
            bindChunk: null,
            preferPrepareOnce: true);

    private static InquiryBatchCommand<int> PreferredChunkCapableCommand()
        => new(
            "work",
            static (target, item) =>
            {
                var parameter = target.CreateParameter();
                parameter.ParameterName = "value";
                parameter.Value = item;
                target.AddParameter(parameter);
            },
            CommandType.Text,
            static (command, items) => ((RecordingCommand)command).AffectedRows = items.Count,
            preferPrepareOnce: true);

    private static InquiryBatchCommand<int> AdaptiveTenParameterCommand()
        => new(
            "row-work",
            static (target, item) =>
            {
                for (var i = 0; i < 10; i++)
                {
                    var parameter = target.CreateParameter();
                    parameter.ParameterName = "value" + i;
                    parameter.Value = item;
                    target.AddParameter(parameter);
                }
            },
            static count => "chunk-work-" + count,
            static (command, items) => ((RecordingCommand)command).AffectedRows = items.Count,
            static items => items.Count < 250,
            parametersPerItem: 10,
            maxItemsPerCommand: 1000,
            commandType: CommandType.Text,
            setBasedMaxItemsPerCommand: 210);

    private static InquiryBatchCommand<int> SelectableRowCommand()
        => new(
            "work",
            static (target, item) =>
            {
                var parameter = target.CreateParameter();
                parameter.ParameterName = "value";
                parameter.Value = item;
                target.AddParameter(parameter);
            },
            static count => "chunk-" + count,
            static (_, _) => throw new InvalidOperationException("Chunk binding must not run."),
            static _ => false,
            parametersPerItem: 1);

    private sealed class LifecycleRecordingInterceptor : IInquiryCommandInterceptor
    {
        private readonly CancellationTokenSource? _cancellation;
        private readonly int? _cancelAtValue;

        internal LifecycleRecordingInterceptor(
            CancellationTokenSource? cancellation = null,
            int? cancelAtValue = null)
        {
            _cancellation = cancellation;
            _cancelAtValue = cancelAtValue;
        }

        internal List<string> Events { get; } = new();

        public ValueTask CommandInitializedAsync(
            InquiryCommandContext context,
            CancellationToken cancellationToken = default)
        {
            Events.Add("initialized:" + GetValue(context));
            return ValueTask.CompletedTask;
        }

        public ValueTask CommandExecutingAsync(
            InquiryCommandContext context,
            CancellationToken cancellationToken = default)
        {
            var value = GetValue(context);
            Events.Add("executing:" + value);
            if (value == _cancelAtValue)
            {
                _cancellation!.Cancel();
                throw new OperationCanceledException(cancellationToken);
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask CommandExecutedAsync(
            InquiryCommandExecutedContext context,
            CancellationToken cancellationToken = default)
        {
            Events.Add($"executed:{GetValue(context)}:{context.RecordsAffected}");
            return ValueTask.CompletedTask;
        }

        public ValueTask CommandFailedAsync(
            InquiryCommandFailedContext context,
            CancellationToken cancellationToken = default)
        {
            Events.Add($"failed:{GetValue(context)}:{context.Exception.GetType().Name}");
            return ValueTask.CompletedTask;
        }

        private static int GetValue(InquiryCommandContext context)
            => (int)context.Command.Parameters[0].Value!;
    }

    private sealed class RecordingState
    {
        internal bool BatchCommandsCanCreateParameters { get; init; } = true;
        internal bool CanCreateBatch { get; init; } = true;
        internal int BatchCreateCount { get; set; }
        internal int BatchDisposeCount { get; set; }
        internal int BatchPrepareCount { get; set; }
        internal int CommandCreateCount { get; set; }
        internal int CommandDisposeCount { get; set; }
        internal int CommandExecuteCount { get; set; }
        internal int CommandPrepareCount { get; set; }
        internal int ParameterCreateCount { get; set; }
        internal int TransactionCommitCount { get; set; }
        internal int TransactionRollbackCount { get; set; }
        internal int SavepointCreateCount { get; set; }
        internal int SavepointRollbackCount { get; set; }
        internal int SavepointReleaseCount { get; set; }
        internal bool ThrowOnCommandExecute { get; init; }
        internal bool ThrowOnCommandPrepare { get; init; }
        internal bool CancelOnCommandPrepare { get; init; }
        internal bool ThrowOnBatchExecute { get; init; }
        internal bool ThrowOnCommandDispose { get; init; }
        internal bool ThrowOnRollback { get; init; }
        internal bool ThrowOnTransactionDispose { get; init; }
        internal bool ThrowOnConnectionDispose { get; init; }
        internal List<int> ExecutedBatchSizes { get; } = new();
        internal List<int> InitializedChunkSizes { get; } = new();
        internal List<string> CleanupEvents { get; } = new();
        internal List<DbTransaction?> CommandTransactions { get; } = new();
        internal List<DbTransaction?> BatchTransactions { get; } = new();
    }

    private sealed class RecordingFactory : IInquiryConnectionFactory
    {
        private readonly RecordingState _state;
        private readonly InquiryBatchExecutionMode _mode;
        private readonly bool _supportsPreparation;

        internal RecordingFactory(RecordingState state, InquiryBatchExecutionMode mode, bool supportsPreparation = false)
        {
            _state = state;
            _mode = mode;
            _supportsPreparation = supportsPreparation;
        }

        public InquiryBatchExecutionMode BatchExecutionMode => _mode;
        public bool SupportsPersistentPreparedStatements => _supportsPreparation;

        public ValueTask<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult<DbConnection>(new RecordingConnection(_state));

        public void InitializeBatchChunkCommand(DbCommand command, int itemCount)
            => _state.InitializedChunkSizes.Add(itemCount);
    }

    private sealed class RecordingConnection : DbConnection
    {
        private readonly RecordingState _state;

        internal RecordingConnection(RecordingState state) => _state = state;

        public override bool CanCreateBatch => _state.CanCreateBatch;
        public override string ConnectionString { get; set; } = string.Empty;
        public override string Database => "recording";
        public override string DataSource => "recording";
        public override string ServerVersion => "1";
        public override ConnectionState State => ConnectionState.Open;
        public override void ChangeDatabase(string databaseName) { }
        public override void Close() { }
        public override void Open() { }

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
            => new RecordingTransaction(this, _state);

        protected override DbCommand CreateDbCommand()
        {
            _state.CommandCreateCount++;
            return new RecordingCommand(_state) { Connection = this };
        }

        protected override DbBatch CreateDbBatch()
        {
            _state.BatchCreateCount++;
            return new RecordingBatch(this, _state);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _state.CleanupEvents.Add("connection-dispose");
                if (_state.ThrowOnConnectionDispose) throw new InvalidOperationException("connection dispose failed");
            }

            base.Dispose(disposing);
        }
    }

    private sealed class RecordingTransaction : DbTransaction
    {
        private readonly RecordingConnection _connection;
        private readonly RecordingState _state;

        internal RecordingTransaction(RecordingConnection connection, RecordingState state)
        {
            _connection = connection;
            _state = state;
        }

        public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
        protected override DbConnection DbConnection => _connection;
        public override void Commit() => _state.TransactionCommitCount++;
        public override void Rollback()
        {
            _state.TransactionRollbackCount++;
            _state.CleanupEvents.Add("transaction-rollback");
            if (_state.ThrowOnRollback) throw new InvalidOperationException("transaction rollback failed");
        }
        public override Task CommitAsync(CancellationToken cancellationToken = default)
        {
            _state.TransactionCommitCount++;
            return Task.CompletedTask;
        }

        public override Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            _state.TransactionRollbackCount++;
            _state.CleanupEvents.Add("transaction-rollback");
            if (_state.ThrowOnRollback) throw new InvalidOperationException("transaction rollback failed");
            return Task.CompletedTask;
        }

        public override void Save(string savepointName) => _state.SavepointCreateCount++;
        public override Task SaveAsync(string savepointName, CancellationToken cancellationToken = default)
        {
            _state.SavepointCreateCount++;
            return Task.CompletedTask;
        }

        public override void Rollback(string savepointName) => _state.SavepointRollbackCount++;
        public override Task RollbackAsync(string savepointName, CancellationToken cancellationToken = default)
        {
            _state.SavepointRollbackCount++;
            return Task.CompletedTask;
        }

        public override void Release(string savepointName) => _state.SavepointReleaseCount++;
        public override Task ReleaseAsync(string savepointName, CancellationToken cancellationToken = default)
        {
            _state.SavepointReleaseCount++;
            return Task.CompletedTask;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _state.CleanupEvents.Add("transaction-dispose");
                if (_state.ThrowOnTransactionDispose) throw new InvalidOperationException("transaction dispose failed");
            }

            base.Dispose(disposing);
        }
    }

    private sealed class RecordingCommand : DbCommand
    {
        private readonly RecordingState _state;
        private DbTransaction? _transaction;

        internal RecordingCommand(RecordingState state) => _state = state;
        internal int AffectedRows { get; set; } = 1;

        public override string CommandText { get; set; } = string.Empty;
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }
        protected override DbConnection? DbConnection { get; set; }
        protected override DbParameterCollection DbParameterCollection { get; } = new RecordingParameterCollection();
        protected override DbTransaction? DbTransaction
        {
            get => _transaction;
            set
            {
                _transaction = value;
                _state.CommandTransactions.Add(value);
            }
        }
        public override void Cancel() { }
        public override int ExecuteNonQuery() => Execute();
        public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
            => Task.FromResult(Execute());
        public override object? ExecuteScalar() => null;

        public override void Prepare()
        {
            _state.CommandPrepareCount++;
            if (_state.CancelOnCommandPrepare) throw new OperationCanceledException();
            if (_state.ThrowOnCommandPrepare) throw new InvalidOperationException("command prepare failed");
        }
        public override Task PrepareAsync(CancellationToken cancellationToken = default)
        {
            _state.CommandPrepareCount++;
            if (_state.CancelOnCommandPrepare)
                return Task.FromException(new OperationCanceledException(cancellationToken));
            if (_state.ThrowOnCommandPrepare)
                return Task.FromException(new InvalidOperationException("command prepare failed"));
            return Task.CompletedTask;
        }

        protected override DbParameter CreateDbParameter()
        {
            _state.ParameterCreateCount++;
            return new RecordingParameter();
        }
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _state.CommandDisposeCount++;
                _state.CleanupEvents.Add("command-dispose");
                if (_state.ThrowOnCommandDispose) throw new InvalidOperationException("command dispose failed");
            }
            base.Dispose(disposing);
        }

        private int Execute()
        {
            _state.CommandExecuteCount++;
            if (_state.ThrowOnCommandExecute) throw new InvalidOperationException("command execute failed");
            return AffectedRows;
        }
    }

    private sealed class RecordingBatch : DbBatch
    {
        private readonly RecordingState _state;
        private readonly RecordingBatchCommandCollection _commands = new();
        private DbTransaction? _transaction;

        internal RecordingBatch(DbConnection connection, RecordingState state)
        {
            DbConnection = connection;
            _state = state;
        }

        public override int Timeout { get; set; }
        protected override DbBatchCommandCollection DbBatchCommands => _commands;
        protected override DbConnection? DbConnection { get; set; }
        protected override DbTransaction? DbTransaction
        {
            get => _transaction;
            set
            {
                _transaction = value;
                _state.BatchTransactions.Add(value);
            }
        }
        public override void Cancel() { }
        public override int ExecuteNonQuery() => Execute();
        public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Execute());
        public override object? ExecuteScalar() => null;
        public override Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<object?>(null);
        public override void Prepare() => _state.BatchPrepareCount++;
        public override Task PrepareAsync(CancellationToken cancellationToken = default)
        {
            _state.BatchPrepareCount++;
            return Task.CompletedTask;
        }

        protected override DbBatchCommand CreateDbBatchCommand()
            => new RecordingBatchCommand(_state.BatchCommandsCanCreateParameters);

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => throw new NotSupportedException();
        protected override Task<DbDataReader> ExecuteDbDataReaderAsync(
            CommandBehavior behavior, CancellationToken cancellationToken = default)
            => Task.FromException<DbDataReader>(new NotSupportedException());

        public override void Dispose()
        {
            _state.BatchDisposeCount++;
            base.Dispose();
        }

        private int Execute()
        {
            if (_state.ThrowOnBatchExecute) throw new InvalidOperationException("batch execute failed");
            _state.ExecutedBatchSizes.Add(_commands.Count);
            return _commands.Count;
        }
    }

    private sealed class RecordingBatchCommand : DbBatchCommand
    {
        private readonly bool _canCreateParameter;

        internal RecordingBatchCommand(bool canCreateParameter) => _canCreateParameter = canCreateParameter;

        public override bool CanCreateParameter => _canCreateParameter;
        public override string CommandText { get; set; } = string.Empty;
        public override CommandType CommandType { get; set; }
        public override int RecordsAffected => 1;
        protected override DbParameterCollection DbParameterCollection { get; } = new RecordingParameterCollection();
        public override DbParameter CreateParameter() => new RecordingParameter();
    }

    private sealed class RecordingBatchCommandCollection : DbBatchCommandCollection
    {
        private readonly List<DbBatchCommand> _items = new();

        public override int Count => _items.Count;
        public override bool IsReadOnly => false;
        public override void Add(DbBatchCommand item) => _items.Add(item);
        public override void Clear() => _items.Clear();
        public override bool Contains(DbBatchCommand item) => _items.Contains(item);
        public override void CopyTo(DbBatchCommand[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
        public override IEnumerator<DbBatchCommand> GetEnumerator() => _items.GetEnumerator();
        public override int IndexOf(DbBatchCommand item) => _items.IndexOf(item);
        public override void Insert(int index, DbBatchCommand item) => _items.Insert(index, item);
        public override bool Remove(DbBatchCommand item) => _items.Remove(item);
        public override void RemoveAt(int index) => _items.RemoveAt(index);
        protected override DbBatchCommand GetBatchCommand(int index) => _items[index];
        protected override void SetBatchCommand(int index, DbBatchCommand batchCommand) => _items[index] = batchCommand;
    }

    private sealed class RecordingParameter : DbParameter
    {
        public override DbType DbType { get; set; }
        public override ParameterDirection Direction { get; set; }
        public override bool IsNullable { get; set; }
        public override string ParameterName { get; set; } = string.Empty;
        public override string SourceColumn { get; set; } = string.Empty;
        public override object? Value { get; set; }
        public override bool SourceColumnNullMapping { get; set; }
        public override int Size { get; set; }
        public override void ResetDbType() { }
    }

    private sealed class RecordingParameterCollection : DbParameterCollection
    {
        private readonly List<DbParameter> _items = new();
        public override int Count => _items.Count;
        public override object SyncRoot => _items;
        public override int Add(object value) { _items.Add((DbParameter)value); return _items.Count - 1; }
        public override void AddRange(Array values) { foreach (var value in values) Add(value!); }
        public override void Clear() => _items.Clear();
        public override bool Contains(object value) => _items.Contains((DbParameter)value);
        public override bool Contains(string value) => IndexOf(value) >= 0;
        public override void CopyTo(Array array, int index) => ((ICollection)_items).CopyTo(array, index);
        public override IEnumerator GetEnumerator() => _items.GetEnumerator();
        public override int IndexOf(object value) => _items.IndexOf((DbParameter)value);
        public override int IndexOf(string parameterName)
            => _items.FindIndex(parameter => parameter.ParameterName == parameterName);
        public override void Insert(int index, object value) => _items.Insert(index, (DbParameter)value);
        public override void Remove(object value) => _items.Remove((DbParameter)value);
        public override void RemoveAt(int index) => _items.RemoveAt(index);
        public override void RemoveAt(string parameterName) => _items.RemoveAt(IndexOf(parameterName));
        protected override DbParameter GetParameter(int index) => _items[index];
        protected override DbParameter GetParameter(string parameterName) => _items[IndexOf(parameterName)];
        protected override void SetParameter(int index, DbParameter value) => _items[index] = value;
        protected override void SetParameter(string parameterName, DbParameter value)
            => _items[IndexOf(parameterName)] = value;
    }

    private sealed class ThrowingDisposeEnumerable : IEnumerable<int>
    {
        private readonly IReadOnlyList<int> _items;

        internal ThrowingDisposeEnumerable(IReadOnlyList<int> items) => _items = items;
        public IEnumerator<int> GetEnumerator() => new Enumerator(_items);
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private sealed class Enumerator : IEnumerator<int>
        {
            private readonly IReadOnlyList<int> _items;
            private int _index = -1;

            internal Enumerator(IReadOnlyList<int> items) => _items = items;
            public int Current => _items[_index];
            object IEnumerator.Current => Current;
            public bool MoveNext() => ++_index < _items.Count;
            public void Reset() => throw new NotSupportedException();
            public void Dispose() => throw new InvalidOperationException("enumerator dispose failed");
        }
    }
}
