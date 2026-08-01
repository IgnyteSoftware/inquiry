using Inquiry.Commands;
using Inquiry.Connections;
using Inquiry.Interceptors;
using Inquiry.Pipeline;
using Microsoft.Data.Sqlite;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

namespace Inquiry.Tests;

/// <summary>
/// The success-path cancellation contract on the BATCH paths (#283): a driver can report normal
/// completion for a statement that cancellation actually cut short, and before enforcement the batch
/// loop would keep going and commit. These tests fake that driver shape deterministically — a
/// delegating <see cref="DbCommand"/> cancels the caller's token mid-execute, then completes the real
/// SQLite statement and reports success — and assert the pipeline surfaces
/// <see cref="OperationCanceledException"/> with the caller's token and rolls the batch back.
/// Each test pins a different execution route through <c>InquiryBatchCommandExecutor</c> or the
/// pipelines' intercepted-chunk lambdas.
/// </summary>
public sealed class BatchCancellationRaceTests
{
    [Fact]
    public async Task ReusedCommandLyingSuccessSurfacesCallerTokenAndRollsBack()
    {
        var (connectionString, keeper) = await CreateDatabaseAsync();
        await using var _ = keeper;
        using var cts = new CancellationTokenSource();
        // MaxBatchSize 2 → two chunks; the lie lands on execute #3 (second chunk, first item), so a
        // committed first chunk would be observable if rollback failed.
        var factory = new LyingSuccessConnectionFactory(connectionString, cts, lieOnExecute: 3);
        var pipeline = new InquiryRequestPipeline(factory, Array.Empty<IInquiryCommandInterceptor>(),
            new InquiryOptions { MaxBatchSize = 2 });

        var exception = await Assert.ThrowsAsync<OperationCanceledException>(() => pipeline.ExecuteBatchAsync(
            "INSERT INTO Items (Id, Name) VALUES (@id, @name)",
            new[] { (1, "Alpha"), (2, "Beta"), (3, "Gamma"), (4, "Delta") },
            static (target, item) => BindItem(target, item),
            cts.Token));

        Assert.Equal(cts.Token, exception.CancellationToken);
        Assert.Equal(0, await CountItemsAsync(keeper));
    }

    [Fact]
    public async Task InterceptedPerItemLyingSuccessSurfacesCallerTokenAndRollsBack()
    {
        // An active interceptor reroutes execution through the pipeline's own per-item lambda
        // (ExecuteInterceptedChunkAsync) instead of the executor — a separate enforcement site.
        var (connectionString, keeper) = await CreateDatabaseAsync();
        await using var _ = keeper;
        using var cts = new CancellationTokenSource();
        var factory = new LyingSuccessConnectionFactory(connectionString, cts, lieOnExecute: 2);
        var interceptor = new RouteWitnessInterceptor();
        var pipeline = new InquiryRequestPipeline(factory, new IInquiryCommandInterceptor[] { interceptor });

        var exception = await Assert.ThrowsAsync<OperationCanceledException>(() => pipeline.ExecuteBatchAsync(
            "INSERT INTO Items (Id, Name) VALUES (@id, @name)",
            new[] { (1, "Alpha"), (2, "Beta"), (3, "Gamma") },
            static (target, item) => BindItem(target, item),
            cts.Token));

        Assert.Equal(cts.Token, exception.CancellationToken);
        Assert.True(interceptor.ExecutingCount > 0, "expected the intercepted route, not the executor route");
        Assert.Equal(0, await CountItemsAsync(keeper));
    }

    [Fact]
    public async Task ChunkBoundLyingSuccessSurfacesCallerTokenAndRollsBack()
    {
        // A BindChunk command routes through ExecuteChunkBoundAsync — one execute per chunk.
        var (connectionString, keeper) = await CreateDatabaseAsync();
        await using var _ = keeper;
        using var cts = new CancellationTokenSource();
        var factory = new LyingSuccessConnectionFactory(connectionString, cts, lieOnExecute: 2);
        var pipeline = new InquiryRequestPipeline(factory, Array.Empty<IInquiryCommandInterceptor>(),
            new InquiryOptions { MaxBatchSize = 2 });
        var command = new InquiryBatchCommand<(int Id, string Name)>(CreateInsertText, BindChunk, parametersPerItem: 2);

        var exception = await Assert.ThrowsAsync<OperationCanceledException>(() => pipeline.ExecuteBatchAsync(
            command, new[] { (1, "A"), (2, "B"), (3, "C"), (4, "D") }, cts.Token));

        Assert.Equal(cts.Token, exception.CancellationToken);
        Assert.Equal(0, await CountItemsAsync(keeper));
    }

    [Fact]
    public async Task CommitLyingSuccessAfterInFlightCancellationSurfacesCallerToken()
    {
        // The batch's own COMMIT is a provider await too: a driver lying success for a
        // KILL-interrupted commit would report a fully-committed batch the server rolled back. The
        // fake SQLite transaction genuinely commits, so no row-count assertion — the real-world
        // outcome is exactly the indeterminacy the OCE exists to signal.
        var (connectionString, keeper) = await CreateDatabaseAsync();
        await using var _ = keeper;
        using var cts = new CancellationTokenSource();
        var factory = new LyingSuccessConnectionFactory(
            connectionString, cts, lieOnExecute: int.MaxValue, lieOnCommit: true);
        var pipeline = new InquiryRequestPipeline(factory, Array.Empty<IInquiryCommandInterceptor>());

        var exception = await Assert.ThrowsAsync<OperationCanceledException>(() => pipeline.ExecuteBatchAsync(
            "INSERT INTO Items (Id, Name) VALUES (@id, @name)",
            new[] { (1, "Alpha"), (2, "Beta") },
            static (target, item) => BindItem(target, item),
            cts.Token));

        Assert.Equal(cts.Token, exception.CancellationToken);
    }

    [Fact]
    public async Task NativeErrorAfterInFlightCancellationNormalizesToCallerTokenAndRollsBack()
    {
        // The other driver face: instead of lying success, the cancelled command throws its NATIVE
        // exception (SqlClient's "severe error … Operation cancelled by user"). The batch boundary
        // must normalize it to OCE with the caller's token, driver exception preserved as inner.
        var (connectionString, keeper) = await CreateDatabaseAsync();
        await using var _ = keeper;
        using var cts = new CancellationTokenSource();
        var factory = new LyingSuccessConnectionFactory(connectionString, cts, lieOnExecute: 3, throwNativeError: true);
        var pipeline = new InquiryRequestPipeline(factory, Array.Empty<IInquiryCommandInterceptor>(),
            new InquiryOptions { MaxBatchSize = 2 });

        var exception = await Assert.ThrowsAsync<OperationCanceledException>(() => pipeline.ExecuteBatchAsync(
            "INSERT INTO Items (Id, Name) VALUES (@id, @name)",
            new[] { (1, "Alpha"), (2, "Beta"), (3, "Gamma"), (4, "Delta") },
            static (target, item) => BindItem(target, item),
            cts.Token));

        Assert.Equal(cts.Token, exception.CancellationToken);
        Assert.IsType<InvalidOperationException>(exception.InnerException);
        Assert.Equal(0, await CountItemsAsync(keeper));
    }

    [Fact]
    public async Task NativeErrorWithoutCancellationPassesThroughUnchanged()
    {
        // A genuine batch failure with no cancellation in play keeps its type — the normalization
        // gate is the caller's token, never the mere presence of an exception.
        var (connectionString, keeper) = await CreateDatabaseAsync();
        await using var _ = keeper;
        using var cts = new CancellationTokenSource();
        var factory = new LyingSuccessConnectionFactory(
            connectionString, cts, lieOnExecute: 2, throwNativeError: true, cancelBeforeFailing: false);
        var pipeline = new InquiryRequestPipeline(factory, Array.Empty<IInquiryCommandInterceptor>());

        await Assert.ThrowsAsync<InvalidOperationException>(() => pipeline.ExecuteBatchAsync(
            "INSERT INTO Items (Id, Name) VALUES (@id, @name)",
            new[] { (1, "Alpha"), (2, "Beta") },
            static (target, item) => BindItem(target, item),
            cts.Token));

        Assert.Equal(0, await CountItemsAsync(keeper));
    }

    [Fact]
    public async Task TransactedNativeErrorAfterInFlightCancellationNormalizesToCallerToken()
    {
        var (connectionString, keeper) = await CreateDatabaseAsync();
        await using var _ = keeper;
        using var cts = new CancellationTokenSource();
        var factory = new LyingSuccessConnectionFactory(connectionString, cts, lieOnExecute: 2, throwNativeError: true);

        await using var connection = await factory.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var pipeline = new TransactedInquiryRequestPipeline(
            connection, transaction, Array.Empty<IInquiryCommandInterceptor>(), factory, options: null);

        var exception = await Assert.ThrowsAsync<OperationCanceledException>(() => pipeline.ExecuteBatchAsync(
            "INSERT INTO Items (Id, Name) VALUES (@id, @name)",
            new[] { (1, "Alpha"), (2, "Beta"), (3, "Gamma") },
            static (target, item) => BindItem(target, item),
            cts.Token));

        Assert.Equal(cts.Token, exception.CancellationToken);
        Assert.IsType<InvalidOperationException>(exception.InnerException);
        await transaction.RollbackAsync();
        Assert.Equal(0, await CountItemsAsync(keeper));
    }

    [Fact]
    public async Task TransactedInterceptedLyingSuccessSurfacesCallerTokenWithoutCommitting()
    {
        // The ambient-transaction pipeline runs its intercepted per-item lambda on the caller's
        // connection; the OCE must escape so the caller's scope rolls back instead of committing.
        var (connectionString, keeper) = await CreateDatabaseAsync();
        await using var _ = keeper;
        using var cts = new CancellationTokenSource();
        var factory = new LyingSuccessConnectionFactory(connectionString, cts, lieOnExecute: 2);

        await using var connection = await factory.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var interceptor = new RouteWitnessInterceptor();
        var pipeline = new TransactedInquiryRequestPipeline(
            connection, transaction, new IInquiryCommandInterceptor[] { interceptor }, factory, options: null);

        var exception = await Assert.ThrowsAsync<OperationCanceledException>(() => pipeline.ExecuteBatchAsync(
            "INSERT INTO Items (Id, Name) VALUES (@id, @name)",
            new[] { (1, "Alpha"), (2, "Beta"), (3, "Gamma") },
            static (target, item) => BindItem(target, item),
            cts.Token));

        Assert.Equal(cts.Token, exception.CancellationToken);
        Assert.True(interceptor.ExecutingCount > 0, "expected the intercepted route, not the executor route");
        await transaction.RollbackAsync();
        Assert.Equal(0, await CountItemsAsync(keeper));
    }

    private static string CreateInsertText(int itemCount)
    {
        var values = Enumerable.Range(0, itemCount).Select(i => $"(@id{i}, @name{i})");
        return "INSERT INTO Items (Id, Name) VALUES " + string.Join(", ", values);
    }

    private static void BindChunk(DbCommand command, IReadOnlyList<(int Id, string Name)> chunk)
    {
        for (var i = 0; i < chunk.Count; i++)
        {
            var id = command.CreateParameter();
            id.ParameterName = "@id" + i;
            id.Value = chunk[i].Id;
            command.Parameters.Add(id);

            var name = command.CreateParameter();
            name.ParameterName = "@name" + i;
            name.Value = chunk[i].Name;
            command.Parameters.Add(name);
        }
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

    private static async Task<(string ConnectionString, SqliteConnection Keeper)> CreateDatabaseAsync()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = "InquiryBatchCancel_" + Guid.NewGuid().ToString("N"),
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

    private static async Task<int> CountItemsAsync(SqliteConnection keeper)
    {
        await using var command = keeper.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Items";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    /// <summary>
    /// Present to force the intercepted execution route, and counts invocations so the tests can
    /// PROVE they took it — a change to interceptor-activation semantics that silently rerouted them
    /// to the executor would otherwise leave them green while testing a different site.
    /// </summary>
    private sealed class RouteWitnessInterceptor : IInquiryCommandInterceptor
    {
        private int _executingCount;

        internal int ExecutingCount => Volatile.Read(ref _executingCount);

        public ValueTask CommandExecutingAsync(InquiryCommandContext context, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _executingCount);
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// Opens real SQLite connections whose commands reproduce the lying-success driver shape: on the
    /// Nth ExecuteNonQueryAsync across the factory's lifetime, the command cancels the caller's token
    /// while "in flight", still executes the real statement, and reports its success.
    /// </summary>
    private sealed class LyingSuccessConnectionFactory : IInquiryConnectionFactory
    {
        private readonly string _connectionString;
        private readonly CancellationTokenSource _cancellation;
        private readonly int _lieOnExecute;
        private readonly bool _throwNativeError;
        private readonly bool _cancelBeforeFailing;
        private readonly bool _lieOnCommit;
        private int _executeCount;

        internal LyingSuccessConnectionFactory(
            string connectionString,
            CancellationTokenSource cancellation,
            int lieOnExecute,
            bool throwNativeError = false,
            bool cancelBeforeFailing = true,
            bool lieOnCommit = false)
        {
            _connectionString = connectionString;
            _cancellation = cancellation;
            _lieOnExecute = lieOnExecute;
            _throwNativeError = throwNativeError;
            _cancelBeforeFailing = cancelBeforeFailing;
            _lieOnCommit = lieOnCommit;
        }

        internal bool LieOnCommit => _lieOnCommit;

        internal void CancelNow() => _cancellation.Cancel();

        public InquiryBatchExecutionMode BatchExecutionMode => InquiryBatchExecutionMode.ReusedCommand;

        public async ValueTask<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
        {
            var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            return new LyingConnection(connection, this);
        }

        internal async Task<int> ExecuteAsync(SqliteCommand inner)
        {
            // The token is deliberately NOT forwarded to SQLite: the faked driver "does the work"
            // regardless, exactly like a KILL-interrupted MySQL SLEEP reporting success.
            if (Interlocked.Increment(ref _executeCount) == _lieOnExecute)
            {
                await Task.Yield();
                if (_cancelBeforeFailing) _cancellation.Cancel();
                if (_throwNativeError)
                    throw new InvalidOperationException("A severe error occurred on the current command.");
            }

            return await inner.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    private sealed class LyingConnection : DbConnection
    {
        private readonly SqliteConnection _inner;
        private readonly LyingSuccessConnectionFactory _factory;

        internal LyingConnection(SqliteConnection inner, LyingSuccessConnectionFactory factory)
        {
            _inner = inner;
            _factory = factory;
        }

        [AllowNull]
        public override string ConnectionString { get => _inner.ConnectionString; set => _inner.ConnectionString = value; }
        public override string Database => _inner.Database;
        public override string DataSource => _inner.DataSource!;
        public override string ServerVersion => _inner.ServerVersion;
        public override ConnectionState State => _inner.State;

        public override void ChangeDatabase(string databaseName) => _inner.ChangeDatabase(databaseName);
        public override void Close() => _inner.Close();
        public override void Open() => _inner.Open();
        public override Task OpenAsync(CancellationToken cancellationToken) => _inner.OpenAsync(cancellationToken);

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
        {
            var transaction = _inner.BeginTransaction(isolationLevel);
            return _factory.LieOnCommit ? new LyingTransaction(transaction, _factory) : transaction;
        }

        protected override DbCommand CreateDbCommand() => new LyingCommand(_inner.CreateCommand(), _factory);

        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await _inner.DisposeAsync();
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// A transaction whose CommitAsync cancels the caller's token mid-commit and then reports normal
    /// completion — the commit-time face of the lying-success driver. The inner SQLite transaction
    /// genuinely commits; what is faked is only that the "driver" swallowed the interruption.
    /// </summary>
    private sealed class LyingTransaction : DbTransaction
    {
        private readonly SqliteTransaction _inner;
        private readonly LyingSuccessConnectionFactory _factory;

        internal LyingTransaction(SqliteTransaction inner, LyingSuccessConnectionFactory factory)
        {
            _inner = inner;
            _factory = factory;
        }

        internal SqliteTransaction Inner => _inner;

        public override IsolationLevel IsolationLevel => _inner.IsolationLevel;
        protected override DbConnection? DbConnection => _inner.Connection;

        public override void Commit() => _inner.Commit();

        public override void Rollback()
        {
            // Models a driver that tolerates rollback after the server already resolved the
            // transaction (the realistic aftermath of an interrupted commit) — the fake's inner
            // SQLite transaction HAS committed, and a throwing rollback here would only test the
            // pipeline's cleanup aggregation, not the commit guard this fixture exists for.
            try { _inner.Rollback(); }
            catch (InvalidOperationException) { }
        }

        public override async Task CommitAsync(CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            _factory.CancelNow();
            _inner.Commit();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await _inner.DisposeAsync();
            GC.SuppressFinalize(this);
        }
    }

    private sealed class LyingCommand : DbCommand
    {
        private readonly SqliteCommand _inner;
        private readonly LyingSuccessConnectionFactory _factory;

        internal LyingCommand(SqliteCommand inner, LyingSuccessConnectionFactory factory)
        {
            _inner = inner;
            _factory = factory;
        }

        [AllowNull]
        public override string CommandText { get => _inner.CommandText; set => _inner.CommandText = value; }
        public override int CommandTimeout { get => _inner.CommandTimeout; set => _inner.CommandTimeout = value; }
        public override CommandType CommandType { get => _inner.CommandType; set => _inner.CommandType = value; }
        public override bool DesignTimeVisible { get => _inner.DesignTimeVisible; set => _inner.DesignTimeVisible = value; }
        public override UpdateRowSource UpdatedRowSource { get => _inner.UpdatedRowSource; set => _inner.UpdatedRowSource = value; }
        protected override DbConnection? DbConnection { get => _inner.Connection; set => _inner.Connection = (SqliteConnection?)value; }
        protected override DbParameterCollection DbParameterCollection => _inner.Parameters;
        protected override DbTransaction? DbTransaction
        {
            get => _inner.Transaction;
            set => _inner.Transaction = value is LyingTransaction lying ? lying.Inner : (SqliteTransaction?)value;
        }

        public override void Cancel() => _inner.Cancel();
        public override void Prepare() => _inner.Prepare();
        public override Task PrepareAsync(CancellationToken cancellationToken = default) => _inner.PrepareAsync(cancellationToken);
        protected override DbParameter CreateDbParameter() => _inner.CreateParameter();
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => _inner.ExecuteReader(behavior);
        public override int ExecuteNonQuery() => _inner.ExecuteNonQuery();
        public override object? ExecuteScalar() => _inner.ExecuteScalar();

        public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
            => _factory.ExecuteAsync(_inner);

        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await _inner.DisposeAsync();
            GC.SuppressFinalize(this);
        }
    }
}
