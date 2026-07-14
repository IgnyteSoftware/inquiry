using Inquiry.Commands;
using Inquiry.Connections;
using System.Collections;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace Inquiry.Tests.Shared;

internal sealed record FinalizedBatchCommand(string CommandText, object? Metadata);
internal sealed record ExecutedBatchCommand(string CommandText, object? Metadata);
internal sealed record ExecutedBatch(IReadOnlyList<ExecutedBatchCommand> Commands);

internal sealed class BatchExecutionProbe
{
    private readonly object _gate = new();
    private readonly Func<DbCommand, object?>? _inspectFinalizedCommand;
    private readonly Func<DbBatchCommand, object?>? _inspectExecutedBatchCommand;
    private readonly List<int> _initializedChunkSizes = new();
    private readonly List<int> _executedBatchSizes = new();
    private readonly List<ExecutedBatch> _executedBatches = new();
    private readonly List<FinalizedBatchCommand> _finalizedCommands = new();
    private int _createBatchCount;
    private int _beginTransactionCount;

    internal BatchExecutionProbe(
        Func<DbCommand, object?>? inspectFinalizedCommand = null,
        Func<DbBatchCommand, object?>? inspectExecutedBatchCommand = null)
    {
        _inspectFinalizedCommand = inspectFinalizedCommand;
        _inspectExecutedBatchCommand = inspectExecutedBatchCommand;
    }

    internal int CreateBatchCount => Volatile.Read(ref _createBatchCount);
    internal int BeginTransactionCount => Volatile.Read(ref _beginTransactionCount);

    internal IReadOnlyList<int> InitializedChunkSizes
    {
        get
        {
            lock (_gate) return _initializedChunkSizes.ToArray();
        }
    }

    internal IReadOnlyList<FinalizedBatchCommand> FinalizedCommands
    {
        get
        {
            lock (_gate) return _finalizedCommands.ToArray();
        }
    }

    internal IReadOnlyList<int> ExecutedBatchSizes
    {
        get
        {
            lock (_gate) return _executedBatchSizes.ToArray();
        }
    }

    internal IReadOnlyList<ExecutedBatch> ExecutedBatches
    {
        get
        {
            lock (_gate) return _executedBatches.ToArray();
        }
    }

    internal void RecordBatchCreated() => Interlocked.Increment(ref _createBatchCount);
    internal void RecordTransactionStarted() => Interlocked.Increment(ref _beginTransactionCount);

    internal void RecordBatchExecuted(int itemCount)
    {
        lock (_gate) _executedBatchSizes.Add(itemCount);
    }

    internal void RecordBatchExecuted(DbBatchCommandCollection commands)
    {
        var snapshots = commands.Cast<DbBatchCommand>()
            .Select(command => new ExecutedBatchCommand(
                command.CommandText,
                _inspectExecutedBatchCommand?.Invoke(command)))
            .ToArray();

        lock (_gate)
        {
            _executedBatchSizes.Add(snapshots.Length);
            _executedBatches.Add(new ExecutedBatch(snapshots));
        }
    }

    internal void RecordChunkInitialized(int itemCount)
    {
        lock (_gate) _initializedChunkSizes.Add(itemCount);
    }

    internal void RecordFinalized(DbCommand command)
    {
        var metadata = _inspectFinalizedCommand?.Invoke(command);
        lock (_gate) _finalizedCommands.Add(new FinalizedBatchCommand(command.CommandText, metadata));
    }

    internal void Reset()
    {
        Interlocked.Exchange(ref _createBatchCount, 0);
        Interlocked.Exchange(ref _beginTransactionCount, 0);
        lock (_gate)
        {
            _initializedChunkSizes.Clear();
            _executedBatchSizes.Clear();
            _executedBatches.Clear();
            _finalizedCommands.Clear();
        }
    }

    internal void Decorate(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var descriptorIndex = -1;
        for (var i = 0; i < services.Count; i++)
        {
            if (services[i].ServiceType != typeof(IInquiryConnectionFactory)) continue;
            if (descriptorIndex >= 0)
                throw new InvalidOperationException("Expected exactly one IInquiryConnectionFactory registration to decorate.");
            descriptorIndex = i;
        }

        if (descriptorIndex < 0)
            throw new InvalidOperationException("No IInquiryConnectionFactory registration was found to decorate.");

        var descriptor = services[descriptorIndex];
        if (descriptor.Lifetime != ServiceLifetime.Singleton || descriptor.ImplementationFactory is null)
        {
            throw new InvalidOperationException(
                "The test probe only decorates a singleton IInquiryConnectionFactory registered with an implementation factory.");
        }

        var implementationFactory = descriptor.ImplementationFactory;
        services[descriptorIndex] = ServiceDescriptor.Singleton<IInquiryConnectionFactory>(serviceProvider =>
        {
            if (implementationFactory(serviceProvider) is not IInquiryConnectionFactory inner)
                throw new InvalidOperationException("The IInquiryConnectionFactory implementation factory returned an invalid instance.");
            return new ProbingInquiryConnectionFactory(inner, this);
        });
    }
}

internal sealed class ProbingInquiryConnectionFactory : IInquiryConnectionFactory, IAsyncDisposable, IDisposable
{
    private readonly IInquiryConnectionFactory _inner;
    private readonly BatchExecutionProbe _probe;
    private int _disposed;

    internal ProbingInquiryConnectionFactory(IInquiryConnectionFactory inner, BatchExecutionProbe probe)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
    }

    public bool SupportsPersistentPreparedStatements => _inner.SupportsPersistentPreparedStatements;
    public bool SupportsBatchExecution => _inner.SupportsBatchExecution;
    public InquiryBatchExecutionMode BatchExecutionMode => _inner.BatchExecutionMode;

    public async ValueTask<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connection = await _inner.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return new ProbingDbConnection(connection, _probe);
    }

    public void InitializeCommand(DbCommand command) => _inner.InitializeCommand(command);

    public void FinalizeCommand(DbCommand command)
    {
        _inner.FinalizeCommand(command);
        _probe.RecordFinalized(command);
    }

    public void InitializeBatchChunkCommand(DbCommand command, int itemCount)
    {
        _inner.InitializeBatchChunkCommand(command, itemCount);
        _probe.RecordChunkInitialized(itemCount);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0 && _inner is IDisposable disposable)
            disposable.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        if (_inner is IAsyncDisposable asyncDisposable)
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        else if (_inner is IDisposable disposable)
            disposable.Dispose();
    }
}

internal sealed class ProbingDbConnection : DbConnection
{
    private readonly DbConnection _inner;
    private readonly BatchExecutionProbe _probe;
    private int _disposed;

    internal ProbingDbConnection(DbConnection inner, BatchExecutionProbe probe)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
    }

    public override bool CanCreateBatch => _inner.CanCreateBatch;
    [AllowNull]
    public override string ConnectionString { get => _inner.ConnectionString; set => _inner.ConnectionString = value; }
    public override int ConnectionTimeout => _inner.ConnectionTimeout;
    public override string Database => _inner.Database;
    public override string DataSource => _inner.DataSource;
    public override string ServerVersion => _inner.ServerVersion;
    public override ConnectionState State => _inner.State;

    public override void ChangeDatabase(string databaseName) => _inner.ChangeDatabase(databaseName);
    public override void Close() => _inner.Close();
    public override Task CloseAsync() => _inner.CloseAsync();
    public override void Open() => _inner.Open();
    public override Task OpenAsync(CancellationToken cancellationToken) => _inner.OpenAsync(cancellationToken);
    public override DataTable GetSchema() => _inner.GetSchema();
    public override DataTable GetSchema(string collectionName) => _inner.GetSchema(collectionName);
    public override DataTable GetSchema(string collectionName, string?[] restrictionValues)
        => _inner.GetSchema(collectionName, restrictionValues);

    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
    {
        _probe.RecordTransactionStarted();
        return _inner.BeginTransaction(isolationLevel);
    }

    protected override ValueTask<DbTransaction> BeginDbTransactionAsync(
        IsolationLevel isolationLevel,
        CancellationToken cancellationToken)
    {
        _probe.RecordTransactionStarted();
        return _inner.BeginTransactionAsync(isolationLevel, cancellationToken);
    }

    protected override DbCommand CreateDbCommand() => _inner.CreateCommand();

    protected override DbBatch CreateDbBatch()
    {
        _probe.RecordBatchCreated();
        return new ProbingDbBatch(_inner.CreateBatch(), _probe);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && Interlocked.Exchange(ref _disposed, 1) == 0) _inner.Dispose();
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            await _inner.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }
}

internal sealed class ProbingDbBatch : DbBatch
{
    private readonly DbBatch _inner;
    private readonly BatchExecutionProbe _probe;
    private int _disposed;

    internal ProbingDbBatch(DbBatch inner, BatchExecutionProbe probe)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
    }

    public override int Timeout { get => _inner.Timeout; set => _inner.Timeout = value; }
    protected override DbBatchCommandCollection DbBatchCommands => _inner.BatchCommands;
    protected override DbConnection? DbConnection { get => _inner.Connection; set => _inner.Connection = value; }
    protected override DbTransaction? DbTransaction { get => _inner.Transaction; set => _inner.Transaction = value; }

    public override void Cancel() => _inner.Cancel();

    public override int ExecuteNonQuery()
    {
        var result = _inner.ExecuteNonQuery();
        _probe.RecordBatchExecuted(_inner.BatchCommands);
        return result;
    }

    public override async Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken = default)
    {
        var result = await _inner.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        _probe.RecordBatchExecuted(_inner.BatchCommands);
        return result;
    }

    public override object? ExecuteScalar() => _inner.ExecuteScalar();
    public override Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken = default)
        => _inner.ExecuteScalarAsync(cancellationToken);
    public override void Prepare() => _inner.Prepare();
    public override Task PrepareAsync(CancellationToken cancellationToken = default)
        => _inner.PrepareAsync(cancellationToken);
    protected override DbBatchCommand CreateDbBatchCommand() => _inner.CreateBatchCommand();
    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        => _inner.ExecuteReader(behavior);
    protected override Task<DbDataReader> ExecuteDbDataReaderAsync(
        CommandBehavior behavior,
        CancellationToken cancellationToken = default)
        => _inner.ExecuteReaderAsync(behavior, cancellationToken);

    public override void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0) _inner.Dispose();
        base.Dispose();
    }

    public override async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            await _inner.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }
}

internal sealed class ExecutionBoundaryEnumerable<T> : IEnumerable<T>
{
    private readonly IReadOnlyList<T> _items;
    private readonly Func<int> _readExecutionCount;
    private readonly List<int> _observedExecutionCounts = new();
    private int _enumeratorCreated;

    internal ExecutionBoundaryEnumerable(IReadOnlyList<T> items, Func<int> readExecutionCount)
    {
        _items = items ?? throw new ArgumentNullException(nameof(items));
        _readExecutionCount = readExecutionCount ?? throw new ArgumentNullException(nameof(readExecutionCount));
    }

    internal IReadOnlyList<int> ObservedExecutionCounts => _observedExecutionCounts.ToArray();

    public IEnumerator<T> GetEnumerator()
    {
        if (Interlocked.Increment(ref _enumeratorCreated) != 1)
            throw new InvalidOperationException("The test source can only be enumerated once.");
        return new Enumerator(this);
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private sealed class Enumerator : IEnumerator<T>
    {
        private readonly ExecutionBoundaryEnumerable<T> _owner;
        private int _index = -1;

        internal Enumerator(ExecutionBoundaryEnumerable<T> owner) => _owner = owner;
        public T Current => _owner._items[_index];
        object IEnumerator.Current => Current!;

        public bool MoveNext()
        {
            _owner._observedExecutionCounts.Add(_owner._readExecutionCount());
            return ++_index < _owner._items.Count;
        }

        public void Reset() => throw new NotSupportedException();
        public void Dispose() { }
    }
}

internal sealed class SinglePassCancellingEnumerable<T> : IEnumerable<T>
{
    private readonly IReadOnlyList<T> _items;
    private readonly CancellationTokenSource _cancellation;
    private readonly int _cancelAtMoveNext;
    private int _enumeratorCreated;

    internal SinglePassCancellingEnumerable(
        IReadOnlyList<T> items,
        CancellationTokenSource cancellation,
        int cancelAtMoveNext)
    {
        _items = items ?? throw new ArgumentNullException(nameof(items));
        _cancellation = cancellation ?? throw new ArgumentNullException(nameof(cancellation));
        if (cancelAtMoveNext <= 0) throw new ArgumentOutOfRangeException(nameof(cancelAtMoveNext));
        _cancelAtMoveNext = cancelAtMoveNext;
    }

    internal int GetEnumeratorCount => Volatile.Read(ref _enumeratorCreated);
    internal int MoveNextCount { get; private set; }
    internal int DisposeCount { get; private set; }

    public IEnumerator<T> GetEnumerator()
    {
        if (Interlocked.Increment(ref _enumeratorCreated) != 1)
            throw new InvalidOperationException("The test source can only be enumerated once.");
        return new Enumerator(this);
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private sealed class Enumerator : IEnumerator<T>
    {
        private readonly SinglePassCancellingEnumerable<T> _owner;
        private int _index = -1;

        internal Enumerator(SinglePassCancellingEnumerable<T> owner) => _owner = owner;
        public T Current => _owner._items[_index];
        object IEnumerator.Current => Current!;

        public bool MoveNext()
        {
            _owner.MoveNextCount++;
            if (_owner.MoveNextCount == _owner._cancelAtMoveNext) _owner._cancellation.Cancel();
            return ++_index < _owner._items.Count;
        }

        public void Reset() => throw new NotSupportedException();
        public void Dispose() => _owner.DisposeCount++;
    }
}
