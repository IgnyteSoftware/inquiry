using Inquiry.Connections;
using Inquiry.DependencyInjection;
using Inquiry.Northwind;
using Inquiry.Northwind.Models;
using Inquiry.Northwind.Stores;
using Inquiry.Sqlite.DependencyInjection;
using Inquiry.Sqlite.Tests.Fixtures;
using Inquiry.Stores;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Data;
using System.Data.Common;

namespace Inquiry.Sqlite.Tests;

/// <summary>
/// Hard-edge tests for the transaction state machine, concurrent-on-same-scope behavior, and
/// the documented "custom InquiryStore-derived method opens its own transaction via the
/// inherited Inquiry property" pattern. Complements the happy-path coverage in
/// TransactionIntegrationTests / AmbientTransactionIntegrationTests.
/// </summary>
public sealed class TransactionStateMachineTests
{
    private const string InsertCustomerSql =
        "INSERT INTO Customers (CustomerID, CompanyName, Country) VALUES (@CustomerID, @CompanyName, @Country)";

    // ---- Concurrent transactions on the SAME DI scope (item 1) -----------------------
    //
    // The existing ParallelTransactionsOnSeparateAsyncFlowsDoNotInterfere test in
    // AmbientTransactionIntegrationTests uses TWO separate harnesses (two DI scopes, two
    // DefaultInquiry instances, two AsyncLocal fields). These tests run two transactions
    // on the SAME DefaultInquiry — proving that AsyncLocal correctly isolates Task.Run-forked
    // ExecutionContexts even when they share the underlying inquiry instance.

    [Fact]
    public async Task ConcurrentTransactionsOnSameDIScopeBothCommit()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "concurrent");
        var inquiry = harness.GetRequiredService<IInquiry>();
        var store = harness.GetRequiredService<CustomerStore>();

        var t1 = Task.Run(async () =>
        {
            await using var tx = await inquiry.BeginTransactionAsync();
            await tx.ExecuteAsync(InsertCustomerSql, new { CustomerID = "PAR01", CompanyName = "Parallel 1", Country = "USA" });
            await tx.CommitAsync();
        });
        var t2 = Task.Run(async () =>
        {
            await using var tx = await inquiry.BeginTransactionAsync();
            await tx.ExecuteAsync(InsertCustomerSql, new { CustomerID = "PAR02", CompanyName = "Parallel 2", Country = "USA" });
            await tx.CommitAsync();
        });

        await Task.WhenAll(t1, t2);

        Assert.NotNull(await store.SelectByKeyAsync("PAR01"));
        Assert.NotNull(await store.SelectByKeyAsync("PAR02"));
    }

    [Fact]
    public async Task ConcurrentTransactionsOnSameDIScopeOneCommitsOneRollsBack()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "concurrent");
        var inquiry = harness.GetRequiredService<IInquiry>();
        var store = harness.GetRequiredService<CustomerStore>();

        var committed = Task.Run(async () =>
        {
            await using var tx = await inquiry.BeginTransactionAsync();
            await tx.ExecuteAsync(InsertCustomerSql, new { CustomerID = "KEEP1", CompanyName = "Keep", Country = "USA" });
            await tx.CommitAsync();
        });
        var rolled = Task.Run(async () =>
        {
            await using var tx = await inquiry.BeginTransactionAsync();
            await tx.ExecuteAsync(InsertCustomerSql, new { CustomerID = "GONE1", CompanyName = "Gone", Country = "USA" });
            await tx.RollbackAsync();
        });

        await Task.WhenAll(committed, rolled);

        // The two transactions must be isolated — the rollback in one flow must not affect
        // the commit in the other, and vice versa.
        Assert.NotNull(await store.SelectByKeyAsync("KEEP1"));
        Assert.Null(await store.SelectByKeyAsync("GONE1"));
    }

    [Fact]
    public async Task ManyConcurrentTransactionsAllCommitIndependently()
    {
        // Stress version: 16 transactions running in parallel on the same DefaultInquiry,
        // each inserts a distinct row, all commit. Verifies the AsyncLocal slot mechanism
        // scales to real concurrency.
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "concurrent");
        var inquiry = harness.GetRequiredService<IInquiry>();
        var store = harness.GetRequiredService<CustomerStore>();

        const int N = 16;
        var tasks = Enumerable.Range(0, N).Select(i => Task.Run(async () =>
        {
            await using var tx = await inquiry.BeginTransactionAsync();
            await tx.ExecuteAsync(InsertCustomerSql,
                new { CustomerID = "C" + i.ToString("D4"), CompanyName = "Concurrent " + i, Country = "USA" });
            await tx.CommitAsync();
        }));

        await Task.WhenAll(tasks);

        var all = await store.SelectAllAsync();
        Assert.Equal(N, all.Count);
        for (var i = 0; i < N; i++)
        {
            Assert.Contains(all, c => c.CustomerID == "C" + i.ToString("D4"));
        }
    }

    // ---- Failed Commit / failed Rollback state machine (item 2) ----------------------
    //
    // Uses a FailingTransaction wrapper that throws on Commit (or Rollback). Proves that
    // when the underlying provider call fails, the handle still transitions to the closed
    // state — subsequent tx.X(...) calls throw ObjectDisposedException instead of silently
    // routing to a corrupt transactional pipeline, and DisposeAsync doesn't throw either.

    [Fact]
    public async Task FailedCommitClosesHandleSoSubsequentForwardingCallsThrow()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "fail");
        var inquiry = BuildInquiry(harness.ConnectionString, FailureMode.OnCommit);

        var tx = await inquiry.BeginTransactionAsync();
        await tx.ExecuteAsync(InsertCustomerSql, new { CustomerID = "FAIL1", CompanyName = "Failing", Country = "USA" });

        // The provider commit throws — but the state machine should close the handle anyway.
        await Assert.ThrowsAsync<InvalidOperationException>(() => tx.CommitAsync());

        // Subsequent tx.X(...) must fail-fast (the bug: previously silently auto-committed via default pipeline).
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => tx.ExecuteAsync(InsertCustomerSql, new { CustomerID = "AFTR1", CompanyName = "After", Country = "USA" }));

        // Dispose must not throw — best-effort cleanup of an already-failed transaction.
        await tx.DisposeAsync();
    }

    [Fact]
    public async Task FailedRollbackClosesHandleSoSubsequentForwardingCallsThrow()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "fail");
        var inquiry = BuildInquiry(harness.ConnectionString, FailureMode.OnRollback);

        var tx = await inquiry.BeginTransactionAsync();
        await tx.ExecuteAsync(InsertCustomerSql, new { CustomerID = "FAIL2", CompanyName = "Failing", Country = "USA" });

        await Assert.ThrowsAsync<InvalidOperationException>(() => tx.RollbackAsync());

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => tx.ExecuteAsync(InsertCustomerSql, new { CustomerID = "AFTR2", CompanyName = "After", Country = "USA" }));

        await tx.DisposeAsync();
    }

    [Fact]
    public async Task FailedCommitClearsAmbientSlotSoRootInquiryStillWorks()
    {
        // After a failed commit, the ambient slot's Pipeline must be cleared so that a
        // subsequent BeginTransactionAsync on the same async flow opens a fresh transaction
        // (rather than silently joining the failed one).
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "fail");
        var inquiry = BuildInquiry(harness.ConnectionString, FailureMode.OnCommit);

        var tx1 = await inquiry.BeginTransactionAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => tx1.CommitAsync());
        await tx1.DisposeAsync();

        // The non-failing path (root inquiry, new transaction) should work — meaning the
        // ambient slot from the failed transaction was correctly cleared.
        var tx2 = await inquiry.BeginTransactionAsync();
        // tx2 will also fail at commit (same factory), but it should have OPENED cleanly,
        // which proves the prior failure didn't leave the ambient slot poisoned.
        await Assert.ThrowsAsync<InvalidOperationException>(() => tx2.CommitAsync());
        await tx2.DisposeAsync();
    }

    // ---- Custom InquiryStore-derived method opens transaction via Inquiry (item 4) ---
    //
    // The transactions feature page documents that a class deriving from InquiryStore<T>
    // can call Inquiry.BeginTransactionAsync from a hand-written method (the protected
    // Inquiry property is inherited from the base). The tx that method opens is ambient,
    // so other store calls in the same flow join it automatically. These tests pin that
    // contract.

    [Fact]
    public async Task CustomStoreMethodOpensTransactionAndOtherStoreCallsJoinIt()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Schemas.GeneratedItem, "custom_store");
        var inquiry = harness.GetRequiredService<IInquiry>();
        var items = harness.GetRequiredService<GeneratedItemStore>();
        var atomic = new GeneratedItemAtomicService(inquiry, items);

        var ids = await atomic.UpsertManyAtomicallyAsync(new[]
        {
            new GeneratedItem { Name = "Atomic 1" },
            new GeneratedItem { Name = "Atomic 2" },
            new GeneratedItem { Name = "Atomic 3" },
        }, commit: true);

        Assert.Equal(3, ids.Count);
        foreach (var id in ids) Assert.NotNull(await items.SelectByKeyAsync(id));
    }

    [Fact]
    public async Task CustomStoreMethodTransactionRollbackRevertsOtherStoreCalls()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Schemas.GeneratedItem, "custom_store");
        var inquiry = harness.GetRequiredService<IInquiry>();
        var items = harness.GetRequiredService<GeneratedItemStore>();
        var atomic = new GeneratedItemAtomicService(inquiry, items);

        // commit: false → method begins the tx, upserts, then disposes without committing →
        // the inner store upserts are rolled back.
        var ids = await atomic.UpsertManyAtomicallyAsync(new[]
        {
            new GeneratedItem { Name = "Reverted 1" },
            new GeneratedItem { Name = "Reverted 2" },
        }, commit: false);

        // ids are the auto-generated keys returned by the UpsertReturning calls — but the
        // outer transaction's rollback erased the rows, so SelectByKey returns null for each.
        foreach (var id in ids) Assert.Null(await items.SelectByKeyAsync(id));
    }

    // ---- Helpers --------------------------------------------------------------------

    /// <summary>
    /// Builds an IInquiry whose connection factory wraps SqliteConnection so the resulting
    /// DbTransaction throws on Commit (or Rollback) on demand. Reuses the harness's shared
    /// in-memory database via the supplied connection string.
    /// </summary>
    private static IInquiry BuildInquiry(string connectionString, FailureMode failureMode)
    {
        var services = new ServiceCollection()
            .AddInquiry()
            .AddInquirySqlite(connectionString);

        // Replace the IInquiryConnectionFactory registered by AddInquirySqlite with our
        // failing wrapper. Last-registration-wins for a singleton.
        services.RemoveAll(typeof(IInquiryConnectionFactory));
        services.AddSingleton<IInquiryConnectionFactory>(new FailingConnectionFactory(connectionString, failureMode));

        return services.BuildServiceProvider().GetRequiredService<IInquiry>();
    }

    private enum FailureMode
    {
        None,
        OnCommit,
        OnRollback,
    }

    private sealed class FailingConnectionFactory : IInquiryConnectionFactory
    {
        private readonly string _connectionString;
        private readonly FailureMode _failureMode;

        public FailingConnectionFactory(string connectionString, FailureMode failureMode)
        {
            _connectionString = connectionString;
            _failureMode = failureMode;
        }

        public async ValueTask<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
        {
            var inner = new SqliteConnection(_connectionString);
            await inner.OpenAsync(cancellationToken);
            return new FailingConnection(inner, _failureMode);
        }
    }

    /// <summary>Forwards everything to the inner SqliteConnection except BeginDbTransaction (returns the failing wrapper) and CreateDbCommand (returns a wrapper that unwraps the failing tx when assigning Transaction).</summary>
    private sealed class FailingConnection : DbConnection
    {
        private readonly SqliteConnection _inner;
        private readonly FailureMode _failureMode;

        public FailingConnection(SqliteConnection inner, FailureMode failureMode)
        {
            _inner = inner;
            _failureMode = failureMode;
        }

        public override string ConnectionString { get => _inner.ConnectionString; set => _inner.ConnectionString = value; }
        public override string Database => _inner.Database;
        public override string DataSource => _inner.DataSource;
        public override string ServerVersion => _inner.ServerVersion;
        public override ConnectionState State => _inner.State;
        public override void ChangeDatabase(string databaseName) => _inner.ChangeDatabase(databaseName);
        public override void Close() => _inner.Close();
        public override void Open() => _inner.Open();

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
            => new FailingTransaction(_inner.BeginTransaction(isolationLevel), _failureMode);

        protected override DbCommand CreateDbCommand()
            => new FailingCommand(_inner.CreateCommand());

        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }

        public override ValueTask DisposeAsync() => _inner.DisposeAsync();
    }

    /// <summary>Wraps SqliteCommand. Forwards everything to the inner — the Transaction setter unwraps a FailingTransaction to assign the real underlying SqliteTransaction (which is what the SqliteCommand requires).</summary>
    private sealed class FailingCommand : DbCommand
    {
        private readonly SqliteCommand _inner;
        public FailingCommand(SqliteCommand inner) => _inner = inner;

        public override string CommandText { get => _inner.CommandText; set => _inner.CommandText = value; }
        public override int CommandTimeout { get => _inner.CommandTimeout; set => _inner.CommandTimeout = value; }
        public override CommandType CommandType { get => _inner.CommandType; set => _inner.CommandType = value; }
        public override UpdateRowSource UpdatedRowSource { get => _inner.UpdatedRowSource; set => _inner.UpdatedRowSource = value; }
        public override bool DesignTimeVisible { get => _inner.DesignTimeVisible; set => _inner.DesignTimeVisible = value; }

        protected override DbConnection? DbConnection
        {
            get => _inner.Connection;
            set
            {
                // The pipeline never assigns a Connection on commands it creates, but be safe:
                // accept either the wrapper or a raw SqliteConnection.
                _inner.Connection = value as SqliteConnection;
            }
        }

        protected override DbParameterCollection DbParameterCollection => _inner.Parameters;

        protected override DbTransaction? DbTransaction
        {
            get => _inner.Transaction;
            set
            {
                // The pipeline assigns dbCommand.Transaction = (our wrapper). SqliteCommand requires
                // a SqliteTransaction — so unwrap our wrapper before forwarding.
                if (value is FailingTransaction wrapper)
                {
                    _inner.Transaction = (SqliteTransaction)wrapper.Inner;
                }
                else
                {
                    _inner.Transaction = value as SqliteTransaction;
                }
            }
        }

        public override void Cancel() => _inner.Cancel();
        public override int ExecuteNonQuery() => _inner.ExecuteNonQuery();
        public override object? ExecuteScalar() => _inner.ExecuteScalar();
        public override void Prepare() => _inner.Prepare();
        protected override DbParameter CreateDbParameter() => _inner.CreateParameter();
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => _inner.ExecuteReader(behavior);
        protected override async Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken ct)
            => await _inner.ExecuteReaderAsync(behavior, ct);

        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }

    }

    /// <summary>Wraps a real SqliteTransaction. Throws on Commit or Rollback based on FailureMode.</summary>
    private sealed class FailingTransaction : DbTransaction
    {
        internal DbTransaction Inner { get; }
        private readonly FailureMode _failureMode;

        public FailingTransaction(DbTransaction inner, FailureMode failureMode)
        {
            Inner = inner;
            _failureMode = failureMode;
        }

        public override IsolationLevel IsolationLevel => Inner.IsolationLevel;
        protected override DbConnection? DbConnection => Inner.Connection;

        public override void Commit()
        {
            if (_failureMode == FailureMode.OnCommit)
                throw new InvalidOperationException("Simulated commit failure for tests.");
            Inner.Commit();
        }

        public override async Task CommitAsync(CancellationToken cancellationToken = default)
        {
            if (_failureMode == FailureMode.OnCommit)
                throw new InvalidOperationException("Simulated commit failure for tests.");
            await Inner.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        public override void Rollback()
        {
            if (_failureMode == FailureMode.OnRollback)
                throw new InvalidOperationException("Simulated rollback failure for tests.");
            Inner.Rollback();
        }

        public override async Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            if (_failureMode == FailureMode.OnRollback)
                throw new InvalidOperationException("Simulated rollback failure for tests.");
            await Inner.RollbackAsync(cancellationToken).ConfigureAwait(false);
        }

        // Savepoint primitives — pass through to inner.
        public override void Save(string savepointName) => Inner.Save(savepointName);
        public override Task SaveAsync(string savepointName, CancellationToken ct = default) => Inner.SaveAsync(savepointName, ct);
        public override void Rollback(string savepointName) => Inner.Rollback(savepointName);
        public override Task RollbackAsync(string savepointName, CancellationToken ct = default) => Inner.RollbackAsync(savepointName, ct);
        public override void Release(string savepointName) => Inner.Release(savepointName);
        public override Task ReleaseAsync(string savepointName, CancellationToken ct = default) => Inner.ReleaseAsync(savepointName, ct);

        protected override void Dispose(bool disposing)
        {
            if (disposing) Inner.Dispose();
            base.Dispose(disposing);
        }

        public override ValueTask DisposeAsync() => Inner.DisposeAsync();
    }
}

/// <summary>
/// A derived InquiryStore that opens its own transaction via the inherited Inquiry property,
/// then calls another store inside it. Documents the pattern the transactions feature page
/// describes and proves it works against a real provider. Must be top-level (not nested)
/// because the Inquiry source generator emits at namespace scope. Uses the test-assembly
/// fixture entity <see cref="GeneratedItem"/> so the generator sees the [InquiryTable]
/// metadata in this compilation (it can't see Northwind's entities, which live in a
/// referenced compiled assembly).
/// </summary>
internal sealed partial class GeneratedItemAtomicService : InquiryStore<GeneratedItem>
{
    private readonly GeneratedItemStore _items;

    public GeneratedItemAtomicService(IInquiry inquiry, GeneratedItemStore items) : base(inquiry)
        => _items = items;

    /// <summary>
    /// Inside one transaction: upsert every item via the injected inner store, return the
    /// generated keys. Set <paramref name="commit"/> to false to exercise the dispose-rollback
    /// path; otherwise commit at the end.
    /// </summary>
    public async Task<IReadOnlyList<int>> UpsertManyAtomicallyAsync(IReadOnlyList<GeneratedItem> items, bool commit, CancellationToken cancellationToken = default)
    {
        await using var tx = await Inquiry.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        var ids = new List<int>(items.Count);
        foreach (var item in items)
        {
            var inserted = await _items.UpsertReturningAsync(item, cancellationToken);
            ids.Add(inserted!.Id!.Value);
        }

        if (commit) await tx.CommitAsync(cancellationToken);
        return ids;
    }
}
