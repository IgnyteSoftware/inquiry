using Inquiry;
using Inquiry.Commands;
using Inquiry.Connections;
using Inquiry.Interceptors;
using Inquiry.Materialization;
using Inquiry.Pipeline;
using Inquiry.SqlServer.Parameters;
using System.Collections;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

namespace Inquiry.SqlServer.Tests;

public sealed class TvpResourceLifetimePipelineTests
{
    [Fact]
    public void CommandResourceScopeRemainsAnAllocationFreeValueType()
        => Assert.True(typeof(InquiryCommandResources.CommandResourceScope).IsValueType);

    [Theory]
    [InlineData(FastPath.Stream)]
    [InlineData(FastPath.List)]
    [InlineData(FastPath.Single)]
    [InlineData(FastPath.Scalar)]
    [InlineData(FastPath.Execute)]
    public async Task InterceptorCommandConstructionFailureReleasesFastPathResources(FastPath fastPath)
    {
        var dbCommand = new ThrowingPrepareCommand();
        var resource = new CountingResource();
        var factory = new FakeConnectionFactory(dbCommand, resource);
        var pipeline = new InquiryRequestPipeline(
            factory,
            new IInquiryCommandInterceptor[] { new NoopInterceptor() });

        await Assert.ThrowsAsync<ArgumentException>(() => InvokeWhitespaceFastPathAsync(pipeline, fastPath));

        Assert.True(dbCommand.IsDisposed);
        Assert.NotNull(factory.LastConnection);
        Assert.Equal(1, factory.LastConnection.DisposeCount);
        Assert.Equal(1, resource.DisposeCount);
    }

    [Fact]
    public async Task SequentialBatchRejectsWhitespaceCommandTextBeforeAcquiringResources()
    {
        var dbCommand = new ThrowingPrepareCommand();
        var resource = new CountingResource();
        var factory = new FakeConnectionFactory(dbCommand, resource);
        var pipeline = new InquiryRequestPipeline(
            factory,
            new IInquiryCommandInterceptor[] { new NoopInterceptor() });

        await Assert.ThrowsAsync<ArgumentException>(() => InvokeWhitespaceFastPathAsync(pipeline, FastPath.SequentialBatch));

        Assert.False(dbCommand.IsDisposed);
        Assert.Null(factory.LastConnection);
        Assert.Equal(0, resource.DisposeCount);
    }

    [Fact]
    public async Task PrepareFailureDisposesBoundSourceBeforeCommandDisposal()
    {
        var dbCommand = new ThrowingPrepareCommand();
        var pipeline = new InquiryRequestPipeline(
            new FakeConnectionFactory(dbCommand),
            Array.Empty<IInquiryCommandInterceptor>(),
            new InquiryOptions { PrepareStatements = PreparedStatementMode.Auto });
        var source = new TrackingEnumerable<int>([1, 2]);
        var command = new InquiryCommand(
            "DELETE FROM T WHERE Id IN (SELECT [Value] FROM @ids)",
            value => InquiryTvpParameter.Bind(
                value,
                "@ids",
                source,
                "[dbo].[Inquiry_Tvp_04c62ef046c2b6360a93af873b3bf9acb9f7a1b100290f0d3f9116f1b78abf7c]",
                InquiryTvpDescriptor.Get("int", 0, 10, 0, false)));

        await Assert.ThrowsAsync<PrepareProbeException>(() => pipeline.ExecuteAsync(command));

        Assert.True(dbCommand.PrepareCalled);
        Assert.False(dbCommand.ExecuteCalled);
        Assert.True(dbCommand.IsDisposed);
        Assert.Equal(1, source.DisposeCount);
        Assert.Equal(1, source.GetEnumeratorCount);
    }

    [Fact]
    public async Task OperationAndResourceCleanupFailuresAreAggregatedPrimaryFirst()
    {
        var dbCommand = new ThrowingPrepareCommand();
        var pipeline = new InquiryRequestPipeline(
            new FakeConnectionFactory(dbCommand),
            Array.Empty<IInquiryCommandInterceptor>(),
            new InquiryOptions { PrepareStatements = PreparedStatementMode.Auto });
        var resource = new ThrowingResource();
        var command = new InquiryCommand("DELETE FROM T", value => InquiryCommandResources.Register(value, resource));

        var exception = await Assert.ThrowsAsync<AggregateException>(() => pipeline.ExecuteAsync(command));

        Assert.Collection(exception.InnerExceptions,
            static error => Assert.IsType<PrepareProbeException>(error),
            static error => Assert.IsType<CleanupProbeException>(error));
        Assert.Equal(1, resource.DisposeCount);
        Assert.True(dbCommand.IsDisposed);
    }

    [Fact]
    public async Task GridCleanupAttemptsResourceCommandConnectionAndLeaseAfterFailure()
    {
        var reader = new DataTable().CreateDataReader();
        var command = new ThrowingPrepareCommand();
        var connection = new CountingConnection();
        var lease = new CountingLease();
        var resource = new ThrowingResource();
        InquiryCommandResources.Register(command, resource);
        var grid = new InquiryGridReader(reader, command, connection, lease);

        await Assert.ThrowsAsync<CleanupProbeException>(async () => await grid.DisposeAsync());

        Assert.Equal(1, resource.DisposeCount);
        Assert.True(command.IsDisposed);
        Assert.Equal(1, connection.DisposeCount);
        Assert.Equal(1, lease.DisposeCount);
    }

    private static async Task InvokeWhitespaceFastPathAsync(InquiryRequestPipeline pipeline, FastPath fastPath)
    {
        switch (fastPath)
        {
            case FastPath.Stream:
                await foreach (var _ in pipeline.QueryAsync<ProbeEntity, int, ProbeMaterializer>(
                    " ", 0, static (_, _) => throw new BinderProbeException(), default))
                {
                }
                break;
            case FastPath.List:
                await pipeline.QueryListAsync<ProbeEntity, int, ProbeMaterializer>(
                    " ", 0, static (_, _) => throw new BinderProbeException(), default, default);
                break;
            case FastPath.Single:
                await pipeline.QuerySingleOrDefaultAsync<ProbeEntity, int, ProbeMaterializer>(
                    " ", 0, static (_, _) => throw new BinderProbeException(), default);
                break;
            case FastPath.Scalar:
                await pipeline.ExecuteScalarAsync<int, int>(
                    " ", 0, static (_, _) => throw new BinderProbeException());
                break;
            case FastPath.Execute:
                await pipeline.ExecuteAsync(
                    " ", 0, static (_, _) => throw new BinderProbeException());
                break;
            case FastPath.SequentialBatch:
                await pipeline.ExecuteBatchAsync(
                    " ", new[] { 0 }, static (_, _) => throw new BinderProbeException());
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(fastPath), fastPath, null);
        }
    }

    private sealed class FakeConnectionFactory : IInquiryConnectionFactory
    {
        private readonly ThrowingPrepareCommand _command;
        private readonly IInquiryExecutionResource? _resource;

        public FakeConnectionFactory(ThrowingPrepareCommand command, IInquiryExecutionResource? resource = null)
        {
            _command = command;
            _resource = resource;
        }

        public FakeConnection? LastConnection { get; private set; }
        public bool SupportsPersistentPreparedStatements => true;

        public void InitializeCommand(DbCommand command)
        {
            if (_resource is not null) InquiryCommandResources.Register(command, _resource);
        }

        public ValueTask<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
        {
            LastConnection = new FakeConnection(_command);
            return new(LastConnection);
        }
    }

    private sealed class FakeConnection : DbConnection
    {
        private readonly ThrowingPrepareCommand _command;
        public FakeConnection(ThrowingPrepareCommand command) => _command = command;
        public int DisposeCount { get; private set; }
        protected override DbCommand CreateDbCommand() => _command;
        protected override void Dispose(bool disposing) { if (disposing) DisposeCount++; base.Dispose(disposing); }
        [AllowNull] public override string ConnectionString { get; set; } = string.Empty;
        public override string Database => "test";
        public override string DataSource => "test";
        public override string ServerVersion => "1";
        public override ConnectionState State => ConnectionState.Open;
        public override void ChangeDatabase(string databaseName) { }
        public override void Close() { }
        public override void Open() { }
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();
    }

    private sealed class CountingConnection : DbConnection
    {
        public int DisposeCount { get; private set; }
        protected override void Dispose(bool disposing) { if (disposing) DisposeCount++; base.Dispose(disposing); }
        protected override DbCommand CreateDbCommand() => throw new NotSupportedException();
        [AllowNull] public override string ConnectionString { get; set; } = string.Empty;
        public override string Database => "test";
        public override string DataSource => "test";
        public override string ServerVersion => "1";
        public override ConnectionState State => ConnectionState.Open;
        public override void ChangeDatabase(string databaseName) { }
        public override void Close() { }
        public override void Open() { }
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();
    }

    private sealed class CountingLease : IDisposable
    {
        public int DisposeCount { get; private set; }
        public void Dispose() => DisposeCount++;
    }

    private sealed class ThrowingResource : IInquiryExecutionResource
    {
        public int DisposeCount { get; private set; }
        public void Dispose()
        {
            DisposeCount++;
            throw new CleanupProbeException();
        }
    }

    private sealed class CountingResource : IInquiryExecutionResource
    {
        public int DisposeCount { get; private set; }
        public void Dispose() => DisposeCount++;
    }

    private sealed class NoopInterceptor : IInquiryCommandInterceptor
    {
    }

    private sealed class ProbeEntity
    {
    }

    private readonly struct ProbeMaterializer : IInquiryEntityMaterializer<ProbeEntity>
    {
        public ProbeEntity Materialize(DbDataReader reader) => new();
    }

    private sealed class ThrowingPrepareCommand : DbCommand
    {
        public bool PrepareCalled { get; private set; }
        public bool ExecuteCalled { get; private set; }
        public bool IsDisposed { get; private set; }

        public override Task PrepareAsync(CancellationToken cancellationToken = default)
        {
            PrepareCalled = true;
            throw new PrepareProbeException();
        }

        public override void Prepare() => throw new PrepareProbeException();
        public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
        {
            ExecuteCalled = true;
            return Task.FromResult(0);
        }

        public override int ExecuteNonQuery() { ExecuteCalled = true; return 0; }
        protected override void Dispose(bool disposing) { IsDisposed = true; base.Dispose(disposing); }
        protected override DbParameter CreateDbParameter() => new FakeParameter();
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => throw new NotSupportedException();
        public override object? ExecuteScalar() => throw new NotSupportedException();
        [AllowNull] public override string CommandText { get; set; } = string.Empty;
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }
        protected override DbConnection? DbConnection { get; set; }
        protected override DbParameterCollection DbParameterCollection { get; } = new FakeParameterCollection();
        protected override DbTransaction? DbTransaction { get; set; }
        public override void Cancel() { }
    }

    private sealed class FakeParameter : DbParameter
    {
        public override DbType DbType { get; set; }
        public override ParameterDirection Direction { get; set; }
        public override bool IsNullable { get; set; }
        [AllowNull] public override string ParameterName { get; set; } = string.Empty;
        [AllowNull] public override string SourceColumn { get; set; } = string.Empty;
        public override object? Value { get; set; }
        public override bool SourceColumnNullMapping { get; set; }
        public override int Size { get; set; }
        public override void ResetDbType() { }
    }

    private sealed class FakeParameterCollection : DbParameterCollection
    {
        private readonly List<DbParameter> _items = [];
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
        public override int IndexOf(string parameterName) => _items.FindIndex(value => value.ParameterName == parameterName);
        public override void Insert(int index, object value) => _items.Insert(index, (DbParameter)value);
        public override void Remove(object value) => _items.Remove((DbParameter)value);
        public override void RemoveAt(int index) => _items.RemoveAt(index);
        public override void RemoveAt(string parameterName) { var index = IndexOf(parameterName); if (index >= 0) RemoveAt(index); }
        protected override DbParameter GetParameter(int index) => _items[index];
        protected override DbParameter GetParameter(string parameterName) => _items[IndexOf(parameterName)];
        protected override void SetParameter(int index, DbParameter value) => _items[index] = value;
        protected override void SetParameter(string parameterName, DbParameter value)
        {
            var index = IndexOf(parameterName);
            if (index < 0) _items.Add(value); else _items[index] = value;
        }
    }

    private sealed class TrackingEnumerable<T> : IEnumerable<T>
    {
        private readonly IReadOnlyList<T> _items;
        public TrackingEnumerable(IReadOnlyList<T> items) => _items = items;
        public int GetEnumeratorCount { get; private set; }
        public int DisposeCount { get; private set; }
        public IEnumerator<T> GetEnumerator()
        {
            GetEnumeratorCount++;
            return new Enumerator(this);
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private sealed class Enumerator : IEnumerator<T>
        {
            private readonly TrackingEnumerable<T> _owner;
            private int _index = -1;
            private bool _disposed;
            public Enumerator(TrackingEnumerable<T> owner) => _owner = owner;
            public T Current => _owner._items[_index];
            object? IEnumerator.Current => Current;
            public bool MoveNext() => ++_index < _owner._items.Count;
            public void Reset() => throw new NotSupportedException();
            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _owner.DisposeCount++;
            }
        }
    }

    private sealed class PrepareProbeException : Exception { }
    private sealed class CleanupProbeException : Exception { }
    private sealed class BinderProbeException : Exception { }

    public enum FastPath { Stream, List, Single, Scalar, Execute, SequentialBatch }
}
