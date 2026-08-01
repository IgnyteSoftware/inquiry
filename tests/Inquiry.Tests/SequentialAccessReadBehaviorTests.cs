using Inquiry.Commands;
using Inquiry.Connections;
using Inquiry.Interceptors;
using Inquiry.Materialization;
using Inquiry.Pipeline;
using Microsoft.Data.Sqlite;
using System.Data;
using System.Data.Common;

namespace Inquiry.Tests;

/// <summary>
/// Pins the reader <see cref="CommandBehavior"/> the pipeline passes to <c>ExecuteReaderAsync</c>.
/// Generated struct materializers always opt into <see cref="CommandBehavior.SequentialAccess"/>.
/// Class materializers opt in only through their sequential-safety capability, so arbitrary custom
/// materializers that may read columns out of order remain buffered.
/// </summary>
public sealed class SequentialAccessReadBehaviorTests
{
    [Fact]
    public async Task StructMaterializerListReadUsesSequentialAccess()
    {
        var recorded = new List<CommandBehavior>();
        var (pipeline, keeper) = await CreateSeededPipelineAsync(recorded);
        await using var _ = keeper;

        var list = await pipeline.QueryListAsync<TestItem, TestItemStructMaterializer>(
            new InquiryCommand("SELECT Id, Name, Flag FROM T"),
            new TestItemStructMaterializer());

        Assert.Single(list);
        var behavior = Assert.Single(recorded);
        Assert.Equal(CommandBehavior.SingleResult | CommandBehavior.SequentialAccess, behavior);
    }

    [Fact]
    public async Task StructMaterializerSingleRowReadUsesSequentialAccessWithoutSingleRow()
    {
        // Pre-fix this path passed `SingleResult | SingleRow | SequentialAccess`. SingleRow gives
        // providers permission to stop after the first row, which silently suppresses the
        // QuerySingleOrDefaultAsync "expected zero or one row, but the query returned multiple rows"
        // throw on providers that honour the hint (audit P2 #5). The constant now omits SingleRow so
        // the duplicate-detecting second ReadAsync reliably observes the extra row.
        var recorded = new List<CommandBehavior>();
        var (pipeline, keeper) = await CreateSeededPipelineAsync(recorded);
        await using var _ = keeper;

        var single = await pipeline.QuerySingleOrDefaultAsync<TestItem, TestItemStructMaterializer>(
            new InquiryCommand("SELECT Id, Name, Flag FROM T WHERE Id = 1"),
            new TestItemStructMaterializer());

        Assert.NotNull(single);
        var behavior = Assert.Single(recorded);
        Assert.Equal(CommandBehavior.SingleResult | CommandBehavior.SequentialAccess, behavior);
        Assert.False(behavior.HasFlag(CommandBehavior.SingleRow));
    }

    [Fact]
    public async Task GeneratedKnownSingleReadUsesSingleRowAndSequentialAccess()
    {
        var recorded = new List<CommandBehavior>();
        var (pipeline, keeper) = await CreateSeededPipelineAsync(recorded);
        await using var _ = keeper;

        var single = await pipeline.QueryGeneratedSingleOrDefaultAsync<TestItem, byte, TestItemStructMaterializer>(
            new InquiryGeneratedCommand<byte>(
                "SELECT Id, Name, Flag FROM T WHERE Id = 1",
                default,
                static (_, _) => { }),
            new TestItemStructMaterializer());

        Assert.NotNull(single);
        var behavior = Assert.Single(recorded);
        Assert.Equal(
            CommandBehavior.SingleResult | CommandBehavior.SingleRow | CommandBehavior.SequentialAccess,
            behavior);
    }

    [Fact]
    public async Task GeneratedCommandValidatingSingleRejectsDuplicatesWithoutSingleRow()
    {
        var recorded = new List<CommandBehavior>();
        var (pipeline, keeper) = await CreateSeededPipelineAsync(recorded, includeSecondRow: true);
        await using var _ = keeper;
        var command = new InquiryGeneratedCommand<byte>(
            "SELECT Id, Name, Flag FROM T ORDER BY Id",
            default,
            static (_, _) => { });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            pipeline.QuerySingleOrDefaultAsync<TestItem, byte, TestItemStructMaterializer>(
                command,
                new TestItemStructMaterializer()));

        Assert.Equal(
            "QuerySingleOrDefaultAsync expected zero or one row, but the query returned multiple rows.",
            exception.Message);
        var behavior = Assert.Single(recorded);
        Assert.Equal(CommandBehavior.SingleResult | CommandBehavior.SequentialAccess, behavior);
        Assert.False(behavior.HasFlag(CommandBehavior.SingleRow));
    }

    [Fact]
    public async Task TransactedGeneratedKnownSingleReadUsesSingleRowAndSequentialAccess()
    {
        var recorded = new List<CommandBehavior>();
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = "InquirySeqAccessTx_" + Guid.NewGuid().ToString("N"),
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared,
        }.ToString();
        await using var keeper = new SqliteConnection(connectionString);
        await keeper.OpenAsync();
        await using (var create = keeper.CreateCommand())
        {
            create.CommandText = "CREATE TABLE T (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL, Flag INTEGER NOT NULL);" +
                "INSERT INTO T (Id, Name, Flag) VALUES (1, 'Alpha', 1);";
            await create.ExecuteNonQueryAsync();
        }

        var factory = new RecordingConnectionFactory(connectionString, recorded);
        await using var connection = await factory.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var pipeline = new TransactedInquiryRequestPipeline(
            connection,
            transaction,
            Array.Empty<IInquiryCommandInterceptor>(),
            factory,
            options: null);

        var single = await pipeline.QueryGeneratedSingleOrDefaultAsync<TestItem, byte, TestItemStructMaterializer>(
            new InquiryGeneratedCommand<byte>(
                "SELECT Id, Name, Flag FROM T WHERE Id = 1",
                default,
                static (_, _) => { }),
            new TestItemStructMaterializer());

        Assert.NotNull(single);
        Assert.Equal(
            CommandBehavior.SingleResult | CommandBehavior.SingleRow | CommandBehavior.SequentialAccess,
            Assert.Single(recorded));
    }

    [Fact]
    public async Task TransactedGeneratedCommandValidatingSingleRejectsDuplicatesWithoutSingleRow()
    {
        var recorded = new List<CommandBehavior>();
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = "InquirySeqAccessTxDuplicate_" + Guid.NewGuid().ToString("N"),
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared,
        }.ToString();
        await using var keeper = new SqliteConnection(connectionString);
        await keeper.OpenAsync();
        await using (var create = keeper.CreateCommand())
        {
            create.CommandText = "CREATE TABLE T (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL, Flag INTEGER NOT NULL);" +
                "INSERT INTO T (Id, Name, Flag) VALUES (1, 'Alpha', 1);" +
                "INSERT INTO T (Id, Name, Flag) VALUES (2, 'Beta', 0);";
            await create.ExecuteNonQueryAsync();
        }

        var factory = new RecordingConnectionFactory(connectionString, recorded);
        await using var connection = await factory.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var pipeline = new TransactedInquiryRequestPipeline(
            connection,
            transaction,
            Array.Empty<IInquiryCommandInterceptor>(),
            factory,
            options: null);
        var command = new InquiryGeneratedCommand<byte>(
            "SELECT Id, Name, Flag FROM T ORDER BY Id",
            default,
            static (_, _) => { });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            pipeline.QuerySingleOrDefaultAsync<TestItem, byte, TestItemStructMaterializer>(
                command,
                new TestItemStructMaterializer()));

        Assert.Equal(
            "QuerySingleOrDefaultAsync expected zero or one row, but the query returned multiple rows.",
            exception.Message);
        var behavior = Assert.Single(recorded);
        Assert.Equal(CommandBehavior.SingleResult | CommandBehavior.SequentialAccess, behavior);
        Assert.False(behavior.HasFlag(CommandBehavior.SingleRow));
    }

    [Theory]
    [InlineData(false, false, ClassReadShape.Stream)]
    [InlineData(false, false, ClassReadShape.List)]
    [InlineData(false, false, ClassReadShape.Single)]
    [InlineData(false, true, ClassReadShape.Stream)]
    [InlineData(false, true, ClassReadShape.List)]
    [InlineData(false, true, ClassReadShape.Single)]
    [InlineData(true, false, ClassReadShape.Stream)]
    [InlineData(true, false, ClassReadShape.List)]
    [InlineData(true, false, ClassReadShape.Single)]
    [InlineData(true, true, ClassReadShape.Stream)]
    [InlineData(true, true, ClassReadShape.List)]
    [InlineData(true, true, ClassReadShape.Single)]
    public async Task ClassMaterializerCapabilitySelectsReadBehaviorOnce(
        bool useTransaction,
        bool sequentialSafe,
        ClassReadShape shape)
    {
        var recorded = new List<CommandBehavior>();
        IInquiryEntityMaterializer<TestItem> materializer = sequentialSafe
            ? new GeneratedSafeTestItemMaterializer()
            : new TestItemClassMaterializer();

        Assert.Equal(sequentialSafe, materializer.IsInquirySequentialAccessSafe);

        if (useTransaction)
        {
            await using var scope = await CreateSeededTransactedPipelineAsync(recorded);
            await ExecuteClassReadAsync(scope.Pipeline, materializer, shape);
        }
        else
        {
            var (pipeline, keeper) = await CreateSeededPipelineAsync(recorded);
            await using var _ = keeper;
            await ExecuteClassReadAsync(pipeline, materializer, shape);
        }

        var behavior = Assert.Single(recorded);
        var expected = sequentialSafe
            ? CommandBehavior.SingleResult | CommandBehavior.SequentialAccess
            : CommandBehavior.SingleResult;
        Assert.Equal(expected, behavior);
        Assert.False(behavior.HasFlag(CommandBehavior.SingleRow));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GeneratedSafeClassValidatingSingleRejectsDuplicates(bool useTransaction)
    {
        var recorded = new List<CommandBehavior>();
        var command = new InquiryCommand("SELECT Id, Name, Flag FROM T ORDER BY Id");

        if (useTransaction)
        {
            await using var scope = await CreateSeededTransactedPipelineAsync(recorded, includeSecondRow: true);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                scope.Pipeline.QuerySingleOrDefaultAsync(
                    command,
                    new GeneratedSafeTestItemMaterializer()));
        }
        else
        {
            var (pipeline, keeper) = await CreateSeededPipelineAsync(recorded, includeSecondRow: true);
            await using var _ = keeper;
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                pipeline.QuerySingleOrDefaultAsync(
                    command,
                    new GeneratedSafeTestItemMaterializer()));
        }

        var behavior = Assert.Single(recorded);
        Assert.Equal(CommandBehavior.SingleResult | CommandBehavior.SequentialAccess, behavior);
        Assert.False(behavior.HasFlag(CommandBehavior.SingleRow));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReverseOrdinalCustomMaterializerRetainsBufferedCompatibility(bool useTransaction)
    {
        var recorded = new List<CommandBehavior>();
        IReadOnlyList<TestItem> items;

        if (useTransaction)
        {
            await using var scope = await CreateSeededTransactedPipelineAsync(recorded);
            items = await scope.Pipeline.QueryListAsync(
                new InquiryCommand("SELECT Id, Name, Flag FROM T"),
                new ReverseOrdinalTestItemMaterializer());
        }
        else
        {
            var (pipeline, keeper) = await CreateSeededPipelineAsync(recorded);
            await using var _ = keeper;
            items = await pipeline.QueryListAsync(
                new InquiryCommand("SELECT Id, Name, Flag FROM T"),
                new ReverseOrdinalTestItemMaterializer());
        }

        var item = Assert.Single(items);
        Assert.Equal(new TestItem(1, "Alpha", true), item);
        Assert.Equal(CommandBehavior.SingleResult, Assert.Single(recorded));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SequentialSafetyCapabilityIsReadOncePerCommand(bool useTransaction)
    {
        var recorded = new List<CommandBehavior>();
        var materializer = new CountingCapabilityMaterializer();

        if (useTransaction)
        {
            await using var scope = await CreateSeededTransactedPipelineAsync(recorded);
            Assert.Single(await scope.Pipeline.QueryListAsync(
                new InquiryCommand("SELECT Id, Name, Flag FROM T"), materializer));
        }
        else
        {
            var (pipeline, keeper) = await CreateSeededPipelineAsync(recorded);
            await using var _ = keeper;
            Assert.Single(await pipeline.QueryListAsync(
                new InquiryCommand("SELECT Id, Name, Flag FROM T"), materializer));
        }

        Assert.Equal(1, materializer.CapabilityReadCount);
        Assert.Equal(CommandBehavior.SingleResult | CommandBehavior.SequentialAccess, Assert.Single(recorded));
    }

    [Fact]
    public async Task TransactedCapabilityGetterRunsAfterBusyStateGuard()
    {
        var recorded = new List<CommandBehavior>();
        await using var scope = await CreateSeededTransactedPipelineAsync(recorded);
        await using var enumerator = scope.Pipeline.QueryAsync(
            new InquiryCommand("SELECT Id, Name, Flag FROM T"),
            new TestItemClassMaterializer()).GetAsyncEnumerator();
        Assert.True(await enumerator.MoveNextAsync());

        var materializer = new ThrowingCapabilityMaterializer();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scope.Pipeline.QueryListAsync(
                new InquiryCommand("SELECT Id, Name, Flag FROM T"), materializer));

        Assert.Contains("in flight", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, materializer.CapabilityReadCount);
    }

    [Fact]
    public async Task TransactedCapabilityGetterFailureReleasesInFlightLease()
    {
        var recorded = new List<CommandBehavior>();
        await using var scope = await CreateSeededTransactedPipelineAsync(recorded);
        var materializer = new ThrowingCapabilityMaterializer();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scope.Pipeline.QueryListAsync(
                new InquiryCommand("SELECT Id, Name, Flag FROM T"), materializer));

        Assert.Equal("Simulated capability failure.", exception.Message);
        Assert.Equal(1, materializer.CapabilityReadCount);
        Assert.Single(await scope.Pipeline.QueryListAsync(
            new InquiryCommand("SELECT Id, Name, Flag FROM T"),
            new TestItemClassMaterializer()));
    }

    [Fact]
    public async Task TransactedCapabilityGetterRunsAfterClosedStateGuard()
    {
        var recorded = new List<CommandBehavior>();
        await using var scope = await CreateSeededTransactedPipelineAsync(recorded);
        scope.Pipeline.MarkClosed();
        var materializer = new ThrowingCapabilityMaterializer();

        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            scope.Pipeline.QueryListAsync(
                new InquiryCommand("SELECT Id, Name, Flag FROM T"), materializer));

        Assert.Equal(0, materializer.CapabilityReadCount);
    }

    [Fact]
    public async Task ClassMaterializerSingleRowReadDoesNotUseSingleRow()
    {
        // Same contract as the struct-materializer path (see preceding test) — the class-materializer
        // single-row path also drops SingleRow so the multi-row throw observes the extra row.
        var recorded = new List<CommandBehavior>();
        var (pipeline, keeper) = await CreateSeededPipelineAsync(recorded);
        await using var _ = keeper;

        var single = await pipeline.QuerySingleOrDefaultAsync<TestItem>(
            new InquiryCommand("SELECT Id, Name, Flag FROM T WHERE Id = 1"),
            new TestItemClassMaterializer());

        Assert.NotNull(single);
        var behavior = Assert.Single(recorded);
        Assert.Equal(CommandBehavior.SingleResult, behavior);
        Assert.False(behavior.HasFlag(CommandBehavior.SingleRow));
    }

    [Fact]
    public async Task ClassMaterializerListReadDoesNotUseSequentialAccess()
    {
        var recorded = new List<CommandBehavior>();
        var (pipeline, keeper) = await CreateSeededPipelineAsync(recorded);
        await using var _ = keeper;

        var list = await pipeline.QueryListAsync<TestItem>(
            new InquiryCommand("SELECT Id, Name, Flag FROM T"),
            new TestItemClassMaterializer());

        Assert.Single(list);
        var behavior = Assert.Single(recorded);
        Assert.Equal(CommandBehavior.SingleResult, behavior);
        Assert.False(behavior.HasFlag(CommandBehavior.SequentialAccess));
    }

    // Returns the keeper connection alongside the pipeline: the caller must keep it alive (and dispose
    // it) for the whole test, because a shared-cache in-memory SQLite database exists only while at
    // least one connection to it is open.
    private static async Task<(InquiryRequestPipeline Pipeline, SqliteConnection Keeper)> CreateSeededPipelineAsync(
        List<CommandBehavior> recorded,
        bool includeSecondRow = false)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = "InquirySeqAccess_" + Guid.NewGuid().ToString("N"),
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared,
        }.ToString();

        var keeper = new SqliteConnection(connectionString);
        await keeper.OpenAsync();
        await using (var create = keeper.CreateCommand())
        {
            create.CommandText =
                "CREATE TABLE T (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL, Flag INTEGER NOT NULL);" +
                "INSERT INTO T (Id, Name, Flag) VALUES (1, 'Alpha', 1);" +
                (includeSecondRow ? "INSERT INTO T (Id, Name, Flag) VALUES (2, 'Beta', 0);" : string.Empty);
            await create.ExecuteNonQueryAsync();
        }

        var pipeline = new InquiryRequestPipeline(
            new RecordingConnectionFactory(connectionString, recorded),
            Array.Empty<IInquiryCommandInterceptor>());
        return (pipeline, keeper);
    }

    private static async Task<TransactedPipelineScope> CreateSeededTransactedPipelineAsync(
        List<CommandBehavior> recorded,
        bool includeSecondRow = false)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = "InquirySeqAccessTxClass_" + Guid.NewGuid().ToString("N"),
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared,
        }.ToString();
        var keeper = new SqliteConnection(connectionString);
        await keeper.OpenAsync();
        await using (var create = keeper.CreateCommand())
        {
            create.CommandText =
                "CREATE TABLE T (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL, Flag INTEGER NOT NULL);" +
                "INSERT INTO T (Id, Name, Flag) VALUES (1, 'Alpha', 1);" +
                (includeSecondRow ? "INSERT INTO T (Id, Name, Flag) VALUES (2, 'Beta', 0);" : string.Empty);
            await create.ExecuteNonQueryAsync();
        }

        var factory = new RecordingConnectionFactory(connectionString, recorded);
        var connection = await factory.OpenConnectionAsync();
        var transaction = await connection.BeginTransactionAsync();
        var pipeline = new TransactedInquiryRequestPipeline(
            connection,
            transaction,
            Array.Empty<IInquiryCommandInterceptor>(),
            factory,
            options: null);
        return new TransactedPipelineScope(pipeline, transaction, connection, keeper);
    }

    private static async Task ExecuteClassReadAsync(
        IInquiryRequestPipeline pipeline,
        IInquiryEntityMaterializer<TestItem> materializer,
        ClassReadShape shape)
    {
        switch (shape)
        {
            case ClassReadShape.Stream:
            {
                var count = 0;
                await foreach (var item in pipeline.QueryAsync(
                    new InquiryCommand("SELECT Id, Name, Flag FROM T"), materializer))
                {
                    Assert.Equal(1, item.Id);
                    count++;
                }

                Assert.Equal(1, count);
                break;
            }
            case ClassReadShape.List:
                Assert.Single(await pipeline.QueryListAsync(
                    new InquiryCommand("SELECT Id, Name, Flag FROM T"), materializer));
                break;
            case ClassReadShape.Single:
                Assert.NotNull(await pipeline.QuerySingleOrDefaultAsync(
                    new InquiryCommand("SELECT Id, Name, Flag FROM T WHERE Id = 1"), materializer));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(shape), shape, null);
        }
    }

    private sealed record TestItem(int Id, string Name, bool Flag);

    private static TestItem Materialize(DbDataReader reader)
        => new(reader.GetInt32(0), reader.GetString(1), reader.GetInt32(2) == 1);

    private sealed class TestItemClassMaterializer : IInquiryEntityMaterializer<TestItem>
    {
        public TestItem Materialize(DbDataReader reader) => SequentialAccessReadBehaviorTests.Materialize(reader);
    }

    private sealed class GeneratedSafeTestItemMaterializer : IInquiryEntityMaterializer<TestItem>
    {
        public bool IsInquirySequentialAccessSafe => true;

        public TestItem Materialize(DbDataReader reader) => SequentialAccessReadBehaviorTests.Materialize(reader);
    }

    private sealed class ReverseOrdinalTestItemMaterializer : IInquiryEntityMaterializer<TestItem>
    {
        public TestItem Materialize(DbDataReader reader)
        {
            var flag = reader.GetInt32(2) == 1;
            var name = reader.GetString(1);
            var id = reader.GetInt32(0);
            return new TestItem(id, name, flag);
        }
    }

    private sealed class CountingCapabilityMaterializer : IInquiryEntityMaterializer<TestItem>
    {
        public int CapabilityReadCount { get; private set; }

        public bool IsInquirySequentialAccessSafe
        {
            get
            {
                CapabilityReadCount++;
                return true;
            }
        }

        public TestItem Materialize(DbDataReader reader) => SequentialAccessReadBehaviorTests.Materialize(reader);
    }

    private sealed class ThrowingCapabilityMaterializer : IInquiryEntityMaterializer<TestItem>
    {
        public int CapabilityReadCount { get; private set; }

        public bool IsInquirySequentialAccessSafe
        {
            get
            {
                CapabilityReadCount++;
                throw new InvalidOperationException("Simulated capability failure.");
            }
        }

        public TestItem Materialize(DbDataReader reader) => throw new InvalidOperationException("Not reached.");
    }

    private readonly struct TestItemStructMaterializer : IInquiryEntityMaterializer<TestItem>
    {
        public TestItem Materialize(DbDataReader reader) => SequentialAccessReadBehaviorTests.Materialize(reader);
    }

    private sealed class RecordingConnectionFactory : IInquiryConnectionFactory
    {
        private readonly string _connectionString;
        private readonly List<CommandBehavior> _recorded;

        public RecordingConnectionFactory(string connectionString, List<CommandBehavior> recorded)
        {
            _connectionString = connectionString;
            _recorded = recorded;
        }

        public async ValueTask<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
        {
            var inner = new SqliteConnection(_connectionString);
            await inner.OpenAsync(cancellationToken);
            return new RecordingDbConnection(inner, _recorded);
        }
    }

    private sealed class TransactedPipelineScope : IAsyncDisposable
    {
        private readonly DbTransaction _transaction;
        private readonly DbConnection _connection;
        private readonly SqliteConnection _keeper;

        public TransactedPipelineScope(
            TransactedInquiryRequestPipeline pipeline,
            DbTransaction transaction,
            DbConnection connection,
            SqliteConnection keeper)
        {
            Pipeline = pipeline;
            _transaction = transaction;
            _connection = connection;
            _keeper = keeper;
        }

        public TransactedInquiryRequestPipeline Pipeline { get; }

        public async ValueTask DisposeAsync()
        {
            await _transaction.DisposeAsync();
            await _connection.DisposeAsync();
            await _keeper.DisposeAsync();
        }
    }

    public enum ClassReadShape
    {
        Stream,
        List,
        Single,
    }

    private sealed class RecordingDbConnection : DbConnection
    {
        private readonly SqliteConnection _inner;
        private readonly List<CommandBehavior> _recorded;

        public RecordingDbConnection(SqliteConnection inner, List<CommandBehavior> recorded)
        {
            _inner = inner;
            _recorded = recorded;
        }

        public override string ConnectionString
        {
            get => _inner.ConnectionString;
            set => _inner.ConnectionString = value;
        }

        public override string Database => _inner.Database;

        public override string DataSource => _inner.DataSource;

        public override string ServerVersion => _inner.ServerVersion;

        public override ConnectionState State => _inner.State;

        public override void ChangeDatabase(string databaseName) => _inner.ChangeDatabase(databaseName);

        public override void Close() => _inner.Close();

        public override void Open() => _inner.Open();

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
            => _inner.BeginTransaction(isolationLevel);

        protected override DbCommand CreateDbCommand()
            => new RecordingDbCommand(_inner.CreateCommand(), _recorded);

        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }

        public override ValueTask DisposeAsync() => _inner.DisposeAsync();
    }

    private sealed class RecordingDbCommand : DbCommand
    {
        private readonly SqliteCommand _inner;
        private readonly List<CommandBehavior> _recorded;

        public RecordingDbCommand(SqliteCommand inner, List<CommandBehavior> recorded)
        {
            _inner = inner;
            _recorded = recorded;
        }

        public override string CommandText
        {
            get => _inner.CommandText;
            set => _inner.CommandText = value;
        }

        public override int CommandTimeout
        {
            get => _inner.CommandTimeout;
            set => _inner.CommandTimeout = value;
        }

        public override CommandType CommandType
        {
            get => _inner.CommandType;
            set => _inner.CommandType = value;
        }

        public override UpdateRowSource UpdatedRowSource
        {
            get => _inner.UpdatedRowSource;
            set => _inner.UpdatedRowSource = value;
        }

        public override bool DesignTimeVisible
        {
            get => _inner.DesignTimeVisible;
            set => _inner.DesignTimeVisible = value;
        }

        protected override DbConnection? DbConnection
        {
            get => _inner.Connection;
            set => _inner.Connection = value as SqliteConnection;
        }

        protected override DbParameterCollection DbParameterCollection => _inner.Parameters;

        protected override DbTransaction? DbTransaction
        {
            get => _inner.Transaction;
            set => _inner.Transaction = value as SqliteTransaction;
        }

        public override void Cancel() => _inner.Cancel();

        public override int ExecuteNonQuery() => _inner.ExecuteNonQuery();

        public override object? ExecuteScalar() => _inner.ExecuteScalar();

        public override void Prepare() => _inner.Prepare();

        protected override DbParameter CreateDbParameter() => _inner.CreateParameter();

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        {
            _recorded.Add(behavior);
            return _inner.ExecuteReader(behavior);
        }

        protected override async Task<DbDataReader> ExecuteDbDataReaderAsync(
            CommandBehavior behavior, CancellationToken cancellationToken)
        {
            _recorded.Add(behavior);
            return await _inner.ExecuteReaderAsync(behavior, cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
