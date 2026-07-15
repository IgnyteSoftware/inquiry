using Inquiry.Commands;
using Inquiry.Connections;
using Inquiry.Interceptors;
using Inquiry.Pipeline;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using System.Collections;
using System.Data;
using System.Data.Common;

namespace Inquiry.Tests;

/// <summary>
/// Covers <c>ExecuteBatchAsync</c> — the multi-item write path generated batch helpers use.
/// Microsoft.Data.Sqlite reports <c>CanCreateBatch == false</c>, so these tests exercise the
/// sequential fallback (one connection, one command per item).
/// </summary>
public sealed class ExecuteBatchTests
{
    [Fact]
    public async Task ExecuteBatchSumsRowsAffectedAcrossItems()
    {
        var (connectionString, keeper) = await CreateDatabaseAsync();
        await using var _ = keeper;

        var pipeline = new InquiryRequestPipeline(
            new BatchTestConnectionFactory(connectionString),
            Array.Empty<IInquiryCommandInterceptor>());

        var total = await pipeline.ExecuteBatchAsync(
            "INSERT INTO Items (Id, Name) VALUES (@id, @name)",
            new[] { (1, "Alpha"), (2, "Beta"), (3, "Gamma") },
            static (target, item) => BindItem(target, item));

        Assert.Equal(3, total);
        Assert.Equal(3, await CountItemsAsync(keeper));
    }

    [Fact]
    public async Task EmptyListReturnsZeroWithoutOpeningConnection()
    {
        var (connectionString, keeper) = await CreateDatabaseAsync();
        await using var _ = keeper;

        var factory = new BatchTestConnectionFactory(connectionString);
        var pipeline = new InquiryRequestPipeline(factory, Array.Empty<IInquiryCommandInterceptor>());

        var total = await pipeline.ExecuteBatchAsync(
            "INSERT INTO Items (Id, Name) VALUES (@id, @name)",
            Array.Empty<(int, string)>(),
            static (target, item) => BindItem(target, item));

        Assert.Equal(0, total);
        Assert.Equal(0, factory.OpenCount);
    }

    [Fact]
    public async Task SequentialFallbackFiresInterceptorPerItem()
    {
        var (connectionString, keeper) = await CreateDatabaseAsync();
        await using var _ = keeper;

        var interceptor = new CountingInterceptor();
        var pipeline = new InquiryRequestPipeline(
            new BatchTestConnectionFactory(connectionString),
            new IInquiryCommandInterceptor[] { interceptor });

        await pipeline.ExecuteBatchAsync(
            "INSERT INTO Items (Id, Name) VALUES (@id, @name)",
            new[] { (1, "Alpha"), (2, "Beta"), (3, "Gamma") },
            static (target, item) => BindItem(target, item));

        Assert.Equal(3, interceptor.ExecutingCount);
        Assert.Equal(3, interceptor.ExecutedCount);
    }

    [Fact]
    public async Task DefaultCommandTimeoutAppliesToSequentialFallback()
    {
        var (connectionString, keeper) = await CreateDatabaseAsync();
        await using var _ = keeper;

        var interceptor = new CountingInterceptor();
        var pipeline = new InquiryRequestPipeline(
            new BatchTestConnectionFactory(connectionString),
            new IInquiryCommandInterceptor[] { interceptor },
            new InquiryOptions { DefaultCommandTimeout = TimeSpan.FromSeconds(9) });

        await pipeline.ExecuteBatchAsync(
            "INSERT INTO Items (Id, Name) VALUES (@id, @name)",
            new[] { (1, "Alpha"), (2, "Beta") },
            static (target, item) => BindItem(target, item));

        Assert.Equal(new[] { 9, 9 }, interceptor.ObservedTimeouts);
    }

    [Fact]
    public async Task TransactedPipelineExecutesBatchWithinTransaction()
    {
        var (connectionString, keeper) = await CreateDatabaseAsync();
        await using var _ = keeper;

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        var pipeline = new TransactedInquiryRequestPipeline(
            connection,
            transaction,
            Array.Empty<IInquiryCommandInterceptor>(),
            new BatchTestConnectionFactory(connectionString),
            options: null);

        var total = await pipeline.ExecuteBatchAsync(
            "INSERT INTO Items (Id, Name) VALUES (@id, @name)",
            new[] { (1, "Alpha"), (2, "Beta") },
            static (target, item) => BindItem(target, item));

        Assert.Equal(2, total);

        await transaction.CommitAsync();
        Assert.Equal(2, await CountItemsAsync(keeper));
    }

    [Fact]
    public async Task WholeChunkPathUsesBoundedChunksAndInitializesBeforeBinding()
    {
        var (connectionString, keeper) = await CreateDatabaseAsync();
        await using var _ = keeper;
        var events = new List<string>();
        var factory = new BatchTestConnectionFactory(connectionString, events);
        var options = new InquiryOptions { MaxBatchSize = 2 };
        var pipeline = new InquiryRequestPipeline(factory, Array.Empty<IInquiryCommandInterceptor>(), options);
        options.MaxBatchSize = 5;
        var command = new InquiryBatchCommand<(int Id, string Name)>(
            CreateInsertText,
            (dbCommand, chunk) =>
            {
                events.Add("bind:" + chunk.Count);
                BindChunk(dbCommand, chunk);
            },
            parametersPerItem: 2);

        var total = await pipeline.ExecuteBatchAsync(command,
            new[] { (1, "A"), (2, "B"), (3, "C"), (4, "D"), (5, "E") });

        Assert.Equal(5, total);
        Assert.Equal(new[] { "initialize:2", "bind:2", "initialize:2", "bind:2", "initialize:1", "bind:1" }, events);
        Assert.Equal(5, await CountItemsAsync(keeper));
    }

    [Fact]
    public async Task LaterChunkFailureRollsBackEarlierChunks()
    {
        var (connectionString, keeper) = await CreateDatabaseAsync();
        await using var _ = keeper;
        var pipeline = new InquiryRequestPipeline(
            new BatchTestConnectionFactory(connectionString), Array.Empty<IInquiryCommandInterceptor>(),
            new InquiryOptions { MaxBatchSize = 2 });
        var command = new InquiryBatchCommand<(int Id, string Name)>(CreateInsertText, BindChunk, parametersPerItem: 2);

        await Assert.ThrowsAsync<SqliteException>(() => pipeline.ExecuteBatchAsync(command,
            new[] { (1, "A"), (2, "B"), (3, "C"), (1, "duplicate") }));

        Assert.Equal(0, await CountItemsAsync(keeper));
    }

    [Fact]
    public async Task CancellationDuringLaterChunkRollsBackAndDisposesEnumerator()
    {
        var (connectionString, keeper) = await CreateDatabaseAsync();
        await using var _ = keeper;
        using var cts = new CancellationTokenSource();
        var source = new CancellingEnumerable(cts, cancelAtMove: 3);
        var factory = new BatchTestConnectionFactory(connectionString);
        var pipeline = new InquiryRequestPipeline(factory, Array.Empty<IInquiryCommandInterceptor>(),
            new InquiryOptions { MaxBatchSize = 2 });
        var command = new InquiryBatchCommand<(int Id, string Name)>(CreateInsertText, BindChunk, parametersPerItem: 2);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pipeline.ExecuteBatchAsync(command, source, cts.Token));

        Assert.Equal(3, source.MoveCount);
        Assert.Equal(1, source.DisposeCount);
        Assert.Equal(1, factory.OpenCount);
        Assert.Equal(0, await CountItemsAsync(keeper));
    }

    [Fact]
    public async Task PreCancelledBatchDoesNotEnumerateOrOpenAConnection()
    {
        var (connectionString, keeper) = await CreateDatabaseAsync();
        await using var _ = keeper;
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var source = new CancellingEnumerable(cts, cancelAtMove: int.MaxValue);
        var factory = new BatchTestConnectionFactory(connectionString);
        var pipeline = new InquiryRequestPipeline(factory, Array.Empty<IInquiryCommandInterceptor>());
        var command = new InquiryBatchCommand<(int Id, string Name)>(CreateInsertText, BindChunk, parametersPerItem: 2);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pipeline.ExecuteBatchAsync(command, source, cts.Token));

        Assert.Equal(0, source.MoveCount);
        Assert.Equal(0, source.GetEnumeratorCount);
        Assert.Equal(0, source.DisposeCount);
        Assert.Equal(0, factory.OpenCount);
    }

    [Fact]
    public async Task ReusedFallbackKeepsOneParameterSetAcrossChunks()
    {
        var (connectionString, keeper) = await CreateDatabaseAsync();
        await using var _ = keeper;
        var ids = new List<DbParameter>();
        var names = new List<DbParameter>();
        var pipeline = new InquiryRequestPipeline(
            new BatchTestConnectionFactory(connectionString), Array.Empty<IInquiryCommandInterceptor>(),
            new InquiryOptions { MaxBatchSize = 2 });
        var command = new InquiryBatchCommand<(int Id, string Name)>(
            "INSERT INTO Items (Id, Name) VALUES (@id, @name)",
            (target, item) =>
            {
                var id = target.CreateParameter();
                ids.Add(id);
                id.ParameterName = "@id";
                id.Value = item.Id;
                target.AddParameter(id);
                var name = target.CreateParameter();
                names.Add(name);
                name.ParameterName = "@name";
                name.Value = item.Name;
                target.AddParameter(name);
            });

        await pipeline.ExecuteBatchAsync(command, new[] { (1, "A"), (2, "B"), (3, "C"), (4, "D"), (5, "E") });

        Assert.Single(ids.Distinct(ReferenceEqualityComparer.Instance));
        Assert.Single(names.Distinct(ReferenceEqualityComparer.Instance));
    }

    [Fact]
    public async Task WholeChunkInterceptorsFireOncePerPhysicalChunk()
    {
        var (connectionString, keeper) = await CreateDatabaseAsync();
        await using var _ = keeper;
        var interceptor = new CountingInterceptor();
        var pipeline = new InquiryRequestPipeline(
            new BatchTestConnectionFactory(connectionString), new[] { interceptor },
            new InquiryOptions { MaxBatchSize = 2 });
        var command = new InquiryBatchCommand<(int Id, string Name)>(CreateInsertText, BindChunk, parametersPerItem: 2);

        await pipeline.ExecuteBatchAsync(command, new[] { (1, "A"), (2, "B"), (3, "C"), (4, "D"), (5, "E") });

        Assert.Equal(3, interceptor.ExecutingCount);
        Assert.Equal(3, interceptor.ExecutedCount);
    }

    [Fact]
    public async Task SelectableShapeEvaluatesOncePerChunkAndPreservesInterceptorLifecycle()
    {
        var (connectionString, keeper) = await CreateDatabaseAsync();
        await using var _ = keeper;
        var selections = 0;
        var interceptor = new CountingInterceptor();
        var pipeline = new InquiryRequestPipeline(
            new BatchTestConnectionFactory(connectionString), new[] { interceptor },
            new InquiryOptions { MaxBatchSize = 2 });
        var command = new InquiryBatchCommand<(int Id, string Name)>(
            "INSERT INTO Items (Id, Name) VALUES (@id, @name)",
            BindItem,
            CreateInsertText,
            BindChunk,
            chunk =>
            {
                selections++;
                return chunk.Count > 1;
            },
            parametersPerItem: 2);

        await pipeline.ExecuteBatchAsync(command, new[] { (1, "A"), (2, "B"), (3, "C"), (4, "D"), (5, "E") });

        Assert.Equal(3, selections);
        Assert.Equal(3, interceptor.ExecutingCount);
        Assert.Equal(3, interceptor.ExecutedCount);
        Assert.Equal(5, await CountItemsAsync(keeper));
    }

    [Fact]
    public async Task ActiveInterceptorForcesArrayCapableFixedRowsThroughPerItemLifecycle()
    {
        var (connectionString, keeper) = await CreateDatabaseAsync();
        await using var _ = keeper;
        var interceptor = new CountingInterceptor();
        var pipeline = new InquiryRequestPipeline(
            new BatchTestConnectionFactory(connectionString, mode: InquiryBatchExecutionMode.ArrayBinding),
            new[] { interceptor });
        var command = new InquiryBatchCommand<(int Id, string Name)>(
            "INSERT INTO Items (Id, Name) VALUES (@id, @name)",
            BindItem,
            bindChunk: static (_, _) => throw new InvalidOperationException("Array binder must not run with active interceptors."));

        await pipeline.ExecuteBatchAsync(command, new[] { (1, "A"), (2, "B") });

        Assert.Equal(2, interceptor.ExecutingCount);
        Assert.Equal(2, interceptor.ExecutedCount);
    }

    [Fact]
    public async Task ArrayModeFallsBackToReusedCommandWithoutChunkBinder()
    {
        var (connectionString, keeper) = await CreateDatabaseAsync();
        await using var _ = keeper;
        var factory = new BatchTestConnectionFactory(connectionString, mode: InquiryBatchExecutionMode.ArrayBinding);
        var pipeline = new InquiryRequestPipeline(factory, Array.Empty<IInquiryCommandInterceptor>());
        var command = new InquiryBatchCommand<(int Id, string Name)>(
            "INSERT INTO Items (Id, Name) VALUES (@id, @name)", BindItem);

        await pipeline.ExecuteBatchAsync(command, new[] { (1, "A") });

        Assert.Equal(1, factory.OpenCount);
        Assert.Equal(1, await CountItemsAsync(keeper));
    }

    [Fact]
    public async Task ArrayModeInitializesBeforeChunkBinderAndNeverInvokesRowBinder()
    {
        var (connectionString, keeper) = await CreateDatabaseAsync();
        await using var _ = keeper;
        var events = new List<string>();
        var factory = new BatchTestConnectionFactory(connectionString, events, InquiryBatchExecutionMode.ArrayBinding);
        var pipeline = new InquiryRequestPipeline(factory, Array.Empty<IInquiryCommandInterceptor>(),
            new InquiryOptions { MaxBatchSize = 1 });
        var command = new InquiryBatchCommand<(int Id, string Name)>(
            "INSERT INTO Items (Id, Name) VALUES (@id, @name)",
            static (_, _) => throw new InvalidOperationException("Row binder must not run on array mode."),
            bindChunk: (dbCommand, chunk) =>
            {
                events.Add("bind:" + chunk.Count);
                dbCommand.Parameters.Add(new SqliteParameter("@id", chunk[0].Id));
                dbCommand.Parameters.Add(new SqliteParameter("@name", chunk[0].Name));
            });

        await pipeline.ExecuteBatchAsync(command, new[] { (1, "A"), (2, "B") });

        Assert.Equal(new[] { "initialize:1", "bind:1", "initialize:1", "bind:1" }, events);
    }

    [Fact]
    public async Task AmbientBatchParticipatesInOuterTransactionWithoutCommittingIt()
    {
        var (connectionString, keeper) = await CreateDatabaseAsync();
        await using var _ = keeper;
        var factory = new BatchTestConnectionFactory(connectionString);
        var pipeline = new InquiryRequestPipeline(factory, Array.Empty<IInquiryCommandInterceptor>());
        using var services = new ServiceCollection().BuildServiceProvider();
        var inquiry = new DefaultInquiry(pipeline, factory, Array.Empty<IInquiryCommandInterceptor>(), services);
        await using var transaction = await inquiry.BeginTransactionAsync();
        var command = new InquiryBatchCommand<(int Id, string Name)>(
            "INSERT INTO Items (Id, Name) VALUES (@id, @name)", BindItem);

        await inquiry.ExecuteBatchAsync(command, new[] { (1, "A"), (2, "B") });
        await transaction.RollbackAsync();

        Assert.Equal(0, await CountItemsAsync(keeper));
    }

    [Theory]
    [InlineData("commit")]
    [InlineData("rollback")]
    [InlineData("dispose")]
    public async Task AmbientBatchHoldsLeaseAgainstTerminalOperations(string terminalOperation)
    {
        var (connectionString, keeper) = await CreateDatabaseAsync();
        await using var _ = keeper;
        var blocker = new BlockingInterceptor();
        var interceptors = new IInquiryCommandInterceptor[] { blocker };
        var factory = new BatchTestConnectionFactory(connectionString);
        var pipeline = new InquiryRequestPipeline(factory, interceptors);
        using var services = new ServiceCollection().BuildServiceProvider();
        var inquiry = new DefaultInquiry(pipeline, factory, interceptors, services);
        await using var transaction = await inquiry.BeginTransactionAsync();
        var command = new InquiryBatchCommand<(int Id, string Name)>(
            "INSERT INTO Items (Id, Name) VALUES (@id, @name)", BindItem);

        var batchTask = inquiry.ExecuteBatchAsync(command, new[] { (1, "A"), (2, "B") });
        await blocker.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            if (terminalOperation == "commit") await transaction.CommitAsync();
            else if (terminalOperation == "rollback") await transaction.RollbackAsync();
            else await transaction.DisposeAsync();
        });

        blocker.Release.TrySetResult();
        await batchTask;
        await transaction.CommitAsync();
    }

    [Fact]
    public async Task EmptyThrowingDisposeSourceDoesNotOpenConnection()
    {
        var (connectionString, keeper) = await CreateDatabaseAsync();
        await using var _ = keeper;
        var source = new ThrowingDisposeEnumerable<(int Id, string Name)>(Array.Empty<(int, string)>());
        var factory = new BatchTestConnectionFactory(connectionString);
        var pipeline = new InquiryRequestPipeline(factory, Array.Empty<IInquiryCommandInterceptor>());
        var command = new InquiryBatchCommand<(int Id, string Name)>(
            "INSERT INTO Items (Id, Name) VALUES (@id, @name)", BindItem);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => pipeline.ExecuteBatchAsync(command, source));

        Assert.Equal("enumerator dispose failed", exception.Message);
        Assert.Equal(0, factory.OpenCount);
    }

    [Fact]
    public async Task ThrowingEnumeratorDisposeRollsBackSuccessfulNonAmbientWrites()
    {
        var (connectionString, keeper) = await CreateDatabaseAsync();
        await using var _ = keeper;
        var source = new ThrowingDisposeEnumerable<(int Id, string Name)>(new[] { (1, "A"), (2, "B") });
        var pipeline = new InquiryRequestPipeline(
            new BatchTestConnectionFactory(connectionString), Array.Empty<IInquiryCommandInterceptor>());
        var command = new InquiryBatchCommand<(int Id, string Name)>(
            "INSERT INTO Items (Id, Name) VALUES (@id, @name)", BindItem);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => pipeline.ExecuteBatchAsync(command, source));

        Assert.Equal("enumerator dispose failed", exception.Message);
        Assert.Equal(0, await CountItemsAsync(keeper));
    }

    [Fact]
    public async Task ExecutionAndEnumeratorDisposeFailuresAreAggregatedInOrder()
    {
        var (connectionString, keeper) = await CreateDatabaseAsync();
        await using var _ = keeper;
        var source = new ThrowingDisposeEnumerable<(int Id, string Name)>(new[] { (1, "A"), (1, "duplicate") });
        var pipeline = new InquiryRequestPipeline(
            new BatchTestConnectionFactory(connectionString), Array.Empty<IInquiryCommandInterceptor>());
        var command = new InquiryBatchCommand<(int Id, string Name)>(
            "INSERT INTO Items (Id, Name) VALUES (@id, @name)", BindItem);

        var exception = await Assert.ThrowsAsync<AggregateException>(() => pipeline.ExecuteBatchAsync(command, source));

        Assert.IsType<SqliteException>(exception.InnerExceptions[0]);
        Assert.Equal("enumerator dispose failed", exception.InnerExceptions[1].Message);
        Assert.Equal(0, await CountItemsAsync(keeper));
    }

    [Fact]
    public async Task AmbientLeaseIsHeldUntilEnumeratorDisposeCompletes()
    {
        var (connectionString, keeper) = await CreateDatabaseAsync();
        await using var _ = keeper;
        var disposeEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDispose = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new ThrowingDisposeEnumerable<(int Id, string Name)>(
            new[] { (1, "A"), (2, "B") }, disposeEntered, releaseDispose);
        var factory = new BatchTestConnectionFactory(connectionString);
        var pipeline = new InquiryRequestPipeline(factory, Array.Empty<IInquiryCommandInterceptor>());
        using var services = new ServiceCollection().BuildServiceProvider();
        var inquiry = new DefaultInquiry(pipeline, factory, Array.Empty<IInquiryCommandInterceptor>(), services);
        await using var transaction = await inquiry.BeginTransactionAsync();
        var command = new InquiryBatchCommand<(int Id, string Name)>(
            "INSERT INTO Items (Id, Name) VALUES (@id, @name)", BindItem);

        var batchTask = Task.Run(async () => await inquiry.ExecuteBatchAsync(command, source));
        await disposeEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<InvalidOperationException>(() => transaction.CommitAsync());
        releaseDispose.TrySetResult();
        await Assert.ThrowsAsync<InvalidOperationException>(() => batchTask);
        await transaction.RollbackAsync();
        Assert.Equal(0, await CountItemsAsync(keeper));
    }

    [Fact]
    public async Task HugeBatchBoundDoesNotPreallocateForEmptyEnumerable()
    {
        var (connectionString, keeper) = await CreateDatabaseAsync();
        await using var _ = keeper;
        var factory = new BatchTestConnectionFactory(connectionString);
        var pipeline = new InquiryRequestPipeline(factory, Array.Empty<IInquiryCommandInterceptor>(),
            new InquiryOptions { MaxBatchSize = int.MaxValue });
        var command = new InquiryBatchCommand<(int Id, string Name)>(
            "INSERT INTO Items (Id, Name) VALUES (@id, @name)", BindItem);
        var source = Enumerable.Empty<(int Id, string Name)>().Where(static _ => true);

        Assert.Equal(0, await pipeline.ExecuteBatchAsync(command, source));
        Assert.Equal(0, factory.OpenCount);
    }

    [Fact]
    public async Task NonAmbientBatchBeginsReadCommittedTransaction()
    {
        var connection = new IsolationRecordingConnection();
        var pipeline = new InquiryRequestPipeline(
            new IsolationRecordingFactory(connection), Array.Empty<IInquiryCommandInterceptor>());
        var command = new InquiryBatchCommand<int>("work", static (_, _) => { });

        await Assert.ThrowsAsync<NotSupportedException>(() => pipeline.ExecuteBatchAsync(command, new[] { 1 }));

        Assert.Equal(IsolationLevel.ReadCommitted, connection.RequestedIsolationLevel);
    }

    private static void BindItem(InquiryParameterTarget target, (int Id, string Name) item)
    {
        var id = target.CreateParameter();
        id.ParameterName = "@id";
        id.Value = item.Id;
        target.AddParameter(id);

        var name = target.CreateParameter();
        name.ParameterName = "@name";
        name.Value = item.Name;
        target.AddParameter(name);
    }

    private static string CreateInsertText(int count)
        => "INSERT INTO Items (Id, Name) VALUES " + string.Join(", ",
            Enumerable.Range(0, count).Select(static i => $"(@id{i}, @name{i})"));

    private static void BindChunk(DbCommand command, IReadOnlyList<(int Id, string Name)> items)
    {
        for (var i = 0; i < items.Count; i++)
        {
            command.Parameters.Add(new SqliteParameter("@id" + i, items[i].Id));
            command.Parameters.Add(new SqliteParameter("@name" + i, items[i].Name));
        }
    }

    private static async Task<(string ConnectionString, SqliteConnection Keeper)> CreateDatabaseAsync()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = "InquiryExecuteBatch_" + Guid.NewGuid().ToString("N"),
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared,
        };
        var connectionString = builder.ToString();

        var keeper = new SqliteConnection(connectionString);
        await keeper.OpenAsync();
        await using (var command = keeper.CreateCommand())
        {
            command.CommandText = "CREATE TABLE Items (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL);";
            await command.ExecuteNonQueryAsync();
        }

        return (connectionString, keeper);
    }

    private static async Task<long> CountItemsAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Items";
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private sealed class CountingInterceptor : IInquiryCommandInterceptor
    {
        public int ExecutingCount { get; private set; }

        public int ExecutedCount { get; private set; }

        public List<int> ObservedTimeouts { get; } = new();

        public ValueTask CommandExecutingAsync(InquiryCommandContext context, CancellationToken cancellationToken = default)
        {
            ExecutingCount++;
            ObservedTimeouts.Add(context.Command.CommandTimeout);
            return ValueTask.CompletedTask;
        }

        public ValueTask CommandExecutedAsync(InquiryCommandExecutedContext context, CancellationToken cancellationToken = default)
        {
            ExecutedCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BatchTestConnectionFactory : IInquiryConnectionFactory
    {
        private readonly string _connectionString;
        private int _openCount;
        private readonly List<string>? _events;

        private readonly InquiryBatchExecutionMode? _mode;

        public BatchTestConnectionFactory(
            string connectionString,
            List<string>? events = null,
            InquiryBatchExecutionMode? mode = null)
        {
            _connectionString = connectionString;
            _events = events;
            _mode = mode;
        }

        public int OpenCount => _openCount;

        public InquiryBatchExecutionMode BatchExecutionMode
            => _mode ?? InquiryBatchExecutionMode.ReusedCommand;

        public async ValueTask<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _openCount);
            var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            return connection;
        }

        public void InitializeBatchChunkCommand(DbCommand command, int itemCount)
            => _events?.Add("initialize:" + itemCount);
    }

    private sealed class BlockingInterceptor : IInquiryCommandInterceptor
    {
        private int _entered;
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask CommandExecutingAsync(InquiryCommandContext context, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _entered, 1) != 0) return;
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class CancellingEnumerable : IEnumerable<(int Id, string Name)>
    {
        private readonly CancellationTokenSource _cts;
        private readonly int _cancelAtMove;

        internal CancellingEnumerable(CancellationTokenSource cts, int cancelAtMove)
        {
            _cts = cts;
            _cancelAtMove = cancelAtMove;
        }

        internal int MoveCount { get; private set; }
        internal int DisposeCount { get; private set; }
        internal int GetEnumeratorCount { get; private set; }

        public IEnumerator<(int Id, string Name)> GetEnumerator()
        {
            GetEnumeratorCount++;
            return new Enumerator(this);
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private sealed class Enumerator : IEnumerator<(int Id, string Name)>
        {
            private readonly CancellingEnumerable _owner;

            internal Enumerator(CancellingEnumerable owner) => _owner = owner;
            public (int Id, string Name) Current => (_owner.MoveCount, "Item " + _owner.MoveCount);
            object IEnumerator.Current => Current;

            public bool MoveNext()
            {
                _owner.MoveCount++;
                if (_owner.MoveCount == _owner._cancelAtMove) _owner._cts.Cancel();
                return _owner.MoveCount <= 5;
            }

            public void Reset() => throw new NotSupportedException();
            public void Dispose() => _owner.DisposeCount++;
        }
    }

    private sealed class ThrowingDisposeEnumerable<T> : IEnumerable<T>
    {
        private readonly IReadOnlyList<T> _items;
        private readonly TaskCompletionSource? _disposeEntered;
        private readonly TaskCompletionSource? _releaseDispose;

        internal ThrowingDisposeEnumerable(
            IReadOnlyList<T> items,
            TaskCompletionSource? disposeEntered = null,
            TaskCompletionSource? releaseDispose = null)
        {
            _items = items;
            _disposeEntered = disposeEntered;
            _releaseDispose = releaseDispose;
        }

        public IEnumerator<T> GetEnumerator() => new Enumerator(this);
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private sealed class Enumerator : IEnumerator<T>
        {
            private readonly ThrowingDisposeEnumerable<T> _owner;
            private int _index = -1;

            internal Enumerator(ThrowingDisposeEnumerable<T> owner) => _owner = owner;
            public T Current => _owner._items[_index];
            object IEnumerator.Current => Current!;
            public bool MoveNext() => ++_index < _owner._items.Count;
            public void Reset() => throw new NotSupportedException();

            public void Dispose()
            {
                _owner._disposeEntered?.TrySetResult();
                _owner._releaseDispose?.Task.GetAwaiter().GetResult();
                throw new InvalidOperationException("enumerator dispose failed");
            }
        }
    }

    private sealed class IsolationRecordingFactory : IInquiryConnectionFactory
    {
        private readonly DbConnection _connection;

        internal IsolationRecordingFactory(DbConnection connection) => _connection = connection;

        public ValueTask<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult(_connection);
    }

    private sealed class IsolationRecordingConnection : DbConnection
    {
        public IsolationLevel? RequestedIsolationLevel { get; private set; }
        public override string ConnectionString { get; set; } = string.Empty;
        public override string Database => "recording";
        public override string DataSource => "recording";
        public override string ServerVersion => "1";
        public override ConnectionState State => ConnectionState.Open;
        public override void ChangeDatabase(string databaseName) { }
        public override void Close() { }
        public override void Open() { }

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
        {
            RequestedIsolationLevel = isolationLevel;
            throw new NotSupportedException("recorded");
        }

        protected override DbCommand CreateDbCommand() => throw new NotSupportedException();
    }
}
