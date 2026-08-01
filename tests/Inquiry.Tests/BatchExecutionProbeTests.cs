using Inquiry.Commands;
using Inquiry.Connections;
using Inquiry.Tests.Shared;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using System.Data;
using System.Data.Common;

namespace Inquiry.Tests;

public sealed class BatchExecutionProbeTests
{
    [Fact]
    public async Task DecoratorForwardsCapabilitiesHooksCommandsAndTransactions()
    {
        var inner = new ProbeTestConnectionFactory();
        var probe = new BatchExecutionProbe(command => (command.GetType(), command.CommandTimeout));
        var factory = new ProbingInquiryConnectionFactory(inner, probe);

        Assert.Equal(inner.SupportsPersistentPreparedStatements, factory.SupportsPersistentPreparedStatements);
        Assert.Equal(inner.SupportsBatchExecution, factory.SupportsBatchExecution);
        Assert.Equal(inner.BatchExecutionMode, factory.BatchExecutionMode);

        await using var connection = await factory.OpenConnectionAsync();
        Assert.IsType<ProbingDbConnection>(connection);
        Assert.Equal(inner.LastConnection!.CanCreateBatch, connection.CanCreateBatch);
        Assert.Equal(inner.LastConnection.ConnectionString, connection.ConnectionString);

        await using var command = connection.CreateCommand();
        Assert.Same(inner.LastConnection, command.Connection);
        command.CommandText = "work";

        factory.InitializeCommand(command);
        factory.InitializeBatchChunkCommand(command, 7);
        factory.FinalizeCommand(command);

        Assert.Equal(1, inner.InitializeCommandCount);
        Assert.Equal(1, inner.InitializeChunkCount);
        Assert.Equal(1, inner.FinalizeCommandCount);
        Assert.Equal(new[] { 7 }, probe.InitializedChunkSizes);
        var finalized = Assert.Single(probe.FinalizedCommands);
        Assert.Equal("work /* finalized */", finalized.CommandText);
        Assert.Equal((typeof(SqliteCommand), 17), Assert.IsType<ValueTuple<Type, int>>(finalized.Metadata));

        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        Assert.IsType<SqliteTransaction>(transaction);
        Assert.Same(inner.LastConnection, transaction.Connection);
        await transaction.RollbackAsync();

        Assert.Throws<NotSupportedException>(() => connection.CreateBatch());
        Assert.Equal(1, probe.CreateBatchCount);

        probe.Reset();
        Assert.Equal(0, probe.CreateBatchCount);
        Assert.Empty(probe.InitializedChunkSizes);
        Assert.Empty(probe.FinalizedCommands);
    }

    [Fact]
    public async Task DisposingDecoratorConnectionDisposesInnerConnection()
    {
        var inner = new ProbeTestConnectionFactory();
        var factory = new ProbingInquiryConnectionFactory(inner, new BatchExecutionProbe());
        var connection = await factory.OpenConnectionAsync();

        Assert.Equal(ConnectionState.Open, inner.LastConnection!.State);
        await connection.DisposeAsync();

        Assert.Equal(ConnectionState.Closed, inner.LastConnection.State);
    }

    [Fact]
    public async Task MixedDisposalDelegatesToInnerConnectionExactlyOnce()
    {
        var inner = new DisposalRecordingConnection();
        var connection = new ProbingDbConnection(inner, new BatchExecutionProbe());

        await connection.DisposeAsync();
        connection.Dispose();

        Assert.Equal(1, inner.AsyncDisposeCount);
        Assert.Equal(0, inner.SyncDisposeCount);
    }

    [Fact]
    public async Task MixedFactoryDisposalDelegatesToInnerFactoryExactlyOnce()
    {
        var inner = new DisposalRecordingFactory();
        var factory = new ProbingInquiryConnectionFactory(inner, new BatchExecutionProbe());

        await factory.DisposeAsync();
        factory.Dispose();

        Assert.Equal(1, inner.AsyncDisposeCount);
        Assert.Equal(0, inner.SyncDisposeCount);
    }

    [Fact]
    public async Task DecorateReplacesProviderFactoryAndPreservesOwnedFactoryDisposal()
    {
        var inner = new DisposalRecordingFactory();
        var services = new ServiceCollection();
        services.AddSingleton<IInquiryConnectionFactory>(_ => inner);
        var probe = new BatchExecutionProbe();

        probe.Decorate(services);

        await using (var provider = services.BuildServiceProvider())
        {
            var decorated = provider.GetRequiredService<IInquiryConnectionFactory>();
            Assert.IsType<ProbingInquiryConnectionFactory>(decorated);
            Assert.NotSame(inner, decorated);
        }

        Assert.Equal(1, inner.AsyncDisposeCount);
        Assert.Equal(0, inner.SyncDisposeCount);
    }

    [Fact]
    public void DecorateRejectsUnsupportedProviderRegistrationShape()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IInquiryConnectionFactory>(new ProbeTestConnectionFactory());

        var exception = Assert.Throws<InvalidOperationException>(() => new BatchExecutionProbe().Decorate(services));

        Assert.Contains("implementation factory", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CancellingEnumerableIsSinglePassAndRecordsCancellationAndDisposal()
    {
        using var cancellation = new CancellationTokenSource();
        var source = new SinglePassCancellingEnumerable<int>(new[] { 10, 20, 30 }, cancellation, cancelAtMoveNext: 2);

        using (var enumerator = source.GetEnumerator())
        {
            Assert.True(enumerator.MoveNext());
            Assert.Equal(10, enumerator.Current);
            Assert.False(cancellation.IsCancellationRequested);

            Assert.True(enumerator.MoveNext());
            Assert.Equal(20, enumerator.Current);
            Assert.True(cancellation.IsCancellationRequested);
        }

        Assert.Equal(1, source.GetEnumeratorCount);
        Assert.Equal(2, source.MoveNextCount);
        Assert.Equal(1, source.DisposeCount);
        Assert.Throws<InvalidOperationException>(() => source.GetEnumerator());
    }

    [Fact]
    public async Task NativeBatchDelegatesProviderObjectsAndSnapshotsSuccessfulExecution()
    {
        var inner = new BatchRecordingConnection();
        var probe = new BatchExecutionProbe(
            inspectExecutedBatchCommand: command => command.Parameters.Cast<DbParameter>()
                .Select(parameter => (parameter.ParameterName, parameter.Value))
                .ToArray());
        await using var connection = new ProbingDbConnection(inner, probe);
        await using var batch = connection.CreateBatch();

        Assert.IsType<ProbingDbBatch>(batch);
        Assert.Same(inner, batch.Connection);
        batch.Timeout = 17;
        var first = batch.CreateBatchCommand();
        var second = batch.CreateBatchCommand();
        first.CommandText = "first";
        var parameter = first.CreateParameter();
        parameter.ParameterName = "@id";
        parameter.Value = 5;
        first.Parameters.Add(parameter);
        second.CommandText = "second";
        batch.BatchCommands.Add(first);
        batch.BatchCommands.Add(second);

        Assert.Empty(probe.ExecutedBatches);
        Assert.Equal(2, await batch.ExecuteNonQueryAsync());

        Assert.Same(first, inner.LastBatch!.BatchCommands[0]);
        Assert.Same(second, inner.LastBatch.BatchCommands[1]);
        Assert.Equal(17, inner.LastBatch.Timeout);
        Assert.Equal(1, inner.LastBatch.ExecuteCount);
        Assert.Equal(1, probe.CreateBatchCount);
        Assert.Equal(new[] { 2 }, probe.ExecutedBatchSizes);
        var executed = Assert.Single(probe.ExecutedBatches);
        Assert.Equal(new[] { "first", "second" }, executed.Commands.Select(command => command.CommandText));
        Assert.Equal(
            new[] { ("@id", (object?)5) },
            Assert.IsType<(string ParameterName, object? Value)[]>(executed.Commands[0].Metadata));

        first.CommandText = "changed";
        parameter.Value = 6;
        Assert.Equal("first", executed.Commands[0].CommandText);
        Assert.Equal(
            new[] { ("@id", (object?)5) },
            Assert.IsType<(string ParameterName, object? Value)[]>(executed.Commands[0].Metadata));

        probe.Reset();
        Assert.Empty(probe.ExecutedBatchSizes);
        Assert.Empty(probe.ExecutedBatches);
    }

    [Fact]
    public async Task FailedNativeBatchExecutionIsNotRecorded()
    {
        var inner = new BatchRecordingConnection();
        var probe = new BatchExecutionProbe();
        await using var connection = new ProbingDbConnection(inner, probe);
        await using var batch = connection.CreateBatch();
        batch.BatchCommands.Add(batch.CreateBatchCommand());
        inner.LastBatch!.ExecuteException = new InvalidOperationException("failed");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => batch.ExecuteNonQueryAsync());

        Assert.Equal("failed", exception.Message);
        Assert.Empty(probe.ExecutedBatchSizes);
        Assert.Empty(probe.ExecutedBatches);
    }

    [Fact]
    public void ExecutionBoundaryEnumerableRecordsReadsAgainstCompletedExecutions()
    {
        var executed = 0;
        var source = new ExecutionBoundaryEnumerable<int>(new[] { 1, 2, 3, 4, 5 }, () => executed);

        using (var enumerator = source.GetEnumerator())
        {
            Assert.True(enumerator.MoveNext());
            Assert.True(enumerator.MoveNext());
            executed += 2;
            Assert.True(enumerator.MoveNext());
            Assert.True(enumerator.MoveNext());
            executed += 2;
            Assert.True(enumerator.MoveNext());
            Assert.False(enumerator.MoveNext());
            executed++;
            Assert.False(enumerator.MoveNext());
        }

        Assert.Equal(new[] { 0, 0, 2, 2, 4, 4, 5 }, source.ObservedExecutionCounts);
        Assert.Throws<InvalidOperationException>(() => source.GetEnumerator());
    }

    private sealed class ProbeTestConnectionFactory : IInquiryConnectionFactory
    {
        internal SqliteConnection? LastConnection { get; private set; }
        internal int InitializeCommandCount { get; private set; }
        internal int InitializeChunkCount { get; private set; }
        internal int FinalizeCommandCount { get; private set; }

        public bool SupportsPersistentPreparedStatements => true;
        public bool SupportsBatchExecution => false;
        public InquiryBatchExecutionMode BatchExecutionMode => InquiryBatchExecutionMode.ArrayBinding;

        public async ValueTask<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
        {
            LastConnection = new SqliteConnection("Data Source=:memory:");
            await LastConnection.OpenAsync(cancellationToken);
            return LastConnection;
        }

        public void InitializeCommand(DbCommand command)
        {
            InitializeCommandCount++;
            command.CommandTimeout = 17;
        }

        public void InitializeBatchChunkCommand(DbCommand command, int itemCount)
            => InitializeChunkCount++;

        public void FinalizeCommand(DbCommand command)
        {
            FinalizeCommandCount++;
            command.CommandText += " /* finalized */";
        }
    }

    private sealed class DisposalRecordingConnection : DbConnection
    {
        internal int AsyncDisposeCount { get; private set; }
        internal int SyncDisposeCount { get; private set; }
        public override string ConnectionString { get; set; } = string.Empty;
        public override string Database => "recording";
        public override string DataSource => "recording";
        public override string ServerVersion => "1";
        public override ConnectionState State => ConnectionState.Open;
        public override void ChangeDatabase(string databaseName) { }
        public override void Close() { }
        public override void Open() { }
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();
        protected override DbCommand CreateDbCommand() => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) SyncDisposeCount++;
            base.Dispose(disposing);
        }

        public override ValueTask DisposeAsync()
        {
            AsyncDisposeCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class DisposalRecordingFactory : IInquiryConnectionFactory, IAsyncDisposable, IDisposable
    {
        internal int AsyncDisposeCount { get; private set; }
        internal int SyncDisposeCount { get; private set; }

        public ValueTask<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public void Dispose() => SyncDisposeCount++;

        public ValueTask DisposeAsync()
        {
            AsyncDisposeCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BatchRecordingConnection : DbConnection
    {
        internal BatchRecordingBatch? LastBatch { get; private set; }
        public override bool CanCreateBatch => true;
        public override string ConnectionString { get; set; } = string.Empty;
        public override string Database => "recording";
        public override string DataSource => "recording";
        public override string ServerVersion => "1";
        public override ConnectionState State => ConnectionState.Open;
        public override void ChangeDatabase(string databaseName) { }
        public override void Close() { }
        public override void Open() { }
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();
        protected override DbCommand CreateDbCommand() => throw new NotSupportedException();

        protected override DbBatch CreateDbBatch()
        {
            LastBatch = new BatchRecordingBatch(this);
            return LastBatch;
        }
    }

    private sealed class BatchRecordingBatch : DbBatch
    {
        private readonly BatchRecordingCommandCollection _commands = new();

        internal BatchRecordingBatch(DbConnection connection) => DbConnection = connection;
        internal int ExecuteCount { get; private set; }
        internal Exception? ExecuteException { get; set; }
        public override int Timeout { get; set; }
        protected override DbBatchCommandCollection DbBatchCommands => _commands;
        protected override DbConnection? DbConnection { get; set; }
        protected override DbTransaction? DbTransaction { get; set; }
        public override void Cancel() { }
        public override int ExecuteNonQuery() => Execute();
        public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Execute());
        public override object? ExecuteScalar() => null;
        public override Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<object?>(null);
        public override void Prepare() { }
        public override Task PrepareAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        protected override DbBatchCommand CreateDbBatchCommand() => new BatchRecordingCommand();
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => throw new NotSupportedException();
        protected override Task<DbDataReader> ExecuteDbDataReaderAsync(
            CommandBehavior behavior,
            CancellationToken cancellationToken = default)
            => Task.FromException<DbDataReader>(new NotSupportedException());

        private int Execute()
        {
            ExecuteCount++;
            if (ExecuteException is not null) throw ExecuteException;
            return _commands.Count;
        }
    }

    private sealed class BatchRecordingCommand : DbBatchCommand
    {
        private readonly SqliteCommand _command = new();

        public override string CommandText { get; set; } = string.Empty;
        public override CommandType CommandType { get; set; }
        public override int RecordsAffected => 1;
        protected override DbParameterCollection DbParameterCollection => _command.Parameters;
        public override DbParameter CreateParameter() => _command.CreateParameter();
    }

    private sealed class BatchRecordingCommandCollection : DbBatchCommandCollection
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
}
