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
}
