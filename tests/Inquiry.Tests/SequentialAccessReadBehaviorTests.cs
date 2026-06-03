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
/// The generated-store (struct-materializer) overloads opt into <see cref="CommandBehavior.SequentialAccess"/>
/// to stream columns forward-only (halving large-result allocation, matching Dapper). The ad-hoc
/// (class-materializer) overloads stay buffered, because a caller-supplied
/// <see cref="IInquiryEntityMaterializer{T}"/> may read columns out of order — which SequentialAccess forbids.
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
        List<CommandBehavior> recorded)
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
                "INSERT INTO T (Id, Name, Flag) VALUES (1, 'Alpha', 1);";
            await create.ExecuteNonQueryAsync();
        }

        var pipeline = new InquiryRequestPipeline(
            new RecordingConnectionFactory(connectionString, recorded),
            Array.Empty<IInquiryCommandInterceptor>());
        return (pipeline, keeper);
    }

    private sealed record TestItem(int Id, string Name, bool Flag);

    private static TestItem Materialize(DbDataReader reader)
        => new(reader.GetInt32(0), reader.GetString(1), reader.GetInt32(2) == 1);

    private sealed class TestItemClassMaterializer : IInquiryEntityMaterializer<TestItem>
    {
        public TestItem Materialize(DbDataReader reader) => SequentialAccessReadBehaviorTests.Materialize(reader);
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
