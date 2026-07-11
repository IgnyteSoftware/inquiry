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
    private static FormattableString InsertCustomer(string customerId, string companyName, string country)
        => $"INSERT INTO Customers (CustomerID, CompanyName, Country) VALUES ({customerId}, {companyName}, {country})";

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
            await tx.ExecuteAsync(InsertCustomer("PAR01", "Parallel 1", "USA"));
            await tx.CommitAsync();
        });
        var t2 = Task.Run(async () =>
        {
            await using var tx = await inquiry.BeginTransactionAsync();
            await tx.ExecuteAsync(InsertCustomer("PAR02", "Parallel 2", "USA"));
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
            await tx.ExecuteAsync(InsertCustomer("KEEP1", "Keep", "USA"));
            await tx.CommitAsync();
        });
        var rolled = Task.Run(async () =>
        {
            await using var tx = await inquiry.BeginTransactionAsync();
            await tx.ExecuteAsync(InsertCustomer("GONE1", "Gone", "USA"));
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
            await tx.ExecuteAsync(InsertCustomer("C" + i.ToString("D4"), "Concurrent " + i, "USA"));
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
        await tx.ExecuteAsync(InsertCustomer("FAIL1", "Failing", "USA"));

        // The provider commit throws — but the state machine should close the handle anyway.
        await Assert.ThrowsAsync<InvalidOperationException>(() => tx.CommitAsync());

        // Subsequent tx.X(...) must fail-fast (the bug: previously silently auto-committed via default pipeline).
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => tx.ExecuteAsync(InsertCustomer("AFTR1", "After", "USA")));

        // Dispose must not throw — best-effort cleanup of an already-failed transaction.
        await tx.DisposeAsync();
    }

    [Fact]
    public async Task FailedRollbackClosesHandleSoSubsequentForwardingCallsThrow()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "fail");
        var inquiry = BuildInquiry(harness.ConnectionString, FailureMode.OnRollback);

        var tx = await inquiry.BeginTransactionAsync();
        await tx.ExecuteAsync(InsertCustomer("FAIL2", "Failing", "USA"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => tx.RollbackAsync());

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => tx.ExecuteAsync(InsertCustomer("AFTR2", "After", "USA")));

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

    // ---- Medium-priority defensive edges (audit items #5, #6, #7, #8, #10) -----------

    [Fact]
    public async Task SavepointCreationCancelledLeavesOuterTransactionUsable()
    {
        // Item #5. A cancelled BeginTransactionAsync on the inner (savepoint) path must
        // leave the outer transaction in a consistent state: the in-flight guard released,
        // the slot's Pipeline still pointing at the outer pipeline, and subsequent ops on
        // the outer still working.
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "sp_cancel");
        var inquiry = harness.GetRequiredService<IInquiry>();
        var store = harness.GetRequiredService<CustomerStore>();

        await using var outer = await inquiry.BeginTransactionAsync();
        await outer.ExecuteAsync(InsertCustomer("OUT01", "Outer", "USA"));

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => outer.BeginTransactionAsync(cts.Token));

        // Outer must remain usable. The savepoint creation failed (cancelled before the
        // SAVEPOINT statement ran), so the outer transaction's state is unchanged.
        await outer.ExecuteAsync(InsertCustomer("AFT01", "After Cancel", "USA"));
        await outer.CommitAsync();

        Assert.NotNull(await store.SelectByKeyAsync("OUT01"));
        Assert.NotNull(await store.SelectByKeyAsync("AFT01"));
    }

    [Fact]
    public async Task ConcurrentOperationsOnOneTransactionEitherSerializeOrFailFastWithoutCorruption()
    {
        // Item #6. Fire many concurrent ops on a single transaction via Task.Run (so each
        // launch actually goes to the worker thread pool, not just iterates synchronously
        // through Microsoft.Data.Sqlite which executes in-memory ops too fast to race).
        //
        // The contract we're asserting is provider-portable — no exact ratio of successes
        // vs failures, since that depends on scheduling. What MUST hold:
        //   - Every op either succeeds or fails with the in-flight guard's specific message
        //     (no corruption, no other exception type).
        //   - The committed row count equals the success count (no phantom rows, no losses).
        // On in-memory SQLite, ops may serialize so fast that no op ever hits the guard —
        // that's a property of the provider, not a bug; the test passes either way.
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "concurrent_one_tx");
        var inquiry = harness.GetRequiredService<IInquiry>();
        var store = harness.GetRequiredService<CustomerStore>();
        await using var tx = await inquiry.BeginTransactionAsync();

        const int N = 10;
        var tasks = Enumerable.Range(0, N).Select(i => Task.Run(async () =>
        {
            try
            {
                await tx.ExecuteAsync(InsertCustomer("X" + i.ToString("D4"), "X " + i, "USA"));
                return (Success: true, Exception: (Exception?)null);
            }
            catch (Exception ex)
            {
                return (Success: false, Exception: ex);
            }
        })).ToList();

        var results = await Task.WhenAll(tasks);
        var successes = results.Count(r => r.Success);
        var failures = results.Where(r => !r.Success).ToList();

        // No losses: every op either succeeded or faulted with an observable exception.
        Assert.Equal(N, successes + failures.Count);

        // Any failure must be the in-flight guard — not a corruption or a generic provider error.
        Assert.All(failures, f =>
        {
            Assert.IsType<InvalidOperationException>(f.Exception);
            Assert.Contains("in flight", f.Exception!.Message, StringComparison.OrdinalIgnoreCase);
        });

        await tx.CommitAsync();

        // Committed row count must match the successful-op count.
        var all = await store.SelectAllAsync();
        Assert.Equal(successes, all.Count);
    }

    [Fact]
    public async Task DisposeWhileStreamingReaderIsInFlightFailsFastAndCanBeRetried()
    {
        // Item #7. If the user disposes a transaction while a streaming reader is mid-stream
        // (the in-flight guard is set, but no error has occurred yet), DisposeAsync must
        // fail fast without closing the transaction. Once the reader releases its lease,
        // disposal can be retried and the next inquiry call must work normally.
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "dispose_in_flight");
        var inquiry = harness.GetRequiredService<IInquiry>();
        var store = harness.GetRequiredService<CustomerStore>();

        // Seed a few rows outside the tx so the in-tx streaming SELECT has rows to stream.
        await store.InsertAsync(new Customer { CustomerID = "SEED1", CompanyName = "Seed 1" });
        await store.InsertAsync(new Customer { CustomerID = "SEED2", CompanyName = "Seed 2" });

        var tx = await inquiry.BeginTransactionAsync();
        var streaming = tx.QueryAsync<Customer>(
            $"SELECT CustomerID, CompanyName, ContactName, ContactTitle, Address, City, Region, PostalCode, Country, Phone, Fax FROM Customers");
        var enumerator = streaming.GetAsyncEnumerator();

        try
        {
            Assert.True(await enumerator.MoveNextAsync()); // pulls the first row; in-flight is set

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => await tx.DisposeAsync());
            Assert.Contains("in flight", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            // Release the reader before retrying transaction disposal.
            await enumerator.DisposeAsync();
        }

        await tx.DisposeAsync();

        // After the dispose, the root inquiry must remain fully functional.
        await store.InsertAsync(new Customer { CustomerID = "AFTR1", CompanyName = "After dispose" });
        Assert.NotNull(await store.SelectByKeyAsync("AFTR1"));
    }

    [Fact]
    public async Task DeferredStreamFirstEnumeratedAfterCommitFailsBeforeProviderUse()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "deferred_closed");
        var inquiry = harness.GetRequiredService<IInquiry>();
        var tx = await inquiry.BeginTransactionAsync();
        var stream = tx.QueryAsync<Customer>(
            $"SELECT CustomerID, CompanyName, ContactName, ContactTitle, Address, City, Region, PostalCode, Country, Phone, Fax FROM Customers");

        await tx.CommitAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
        {
            await foreach (var _ in stream) { }
        });
        await tx.DisposeAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeferredRootStreamFirstEnumeratedAfterRollbackOrDisposeFailsBeforeProviderUse(bool dispose)
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "deferred_root_terminal");
        var tx = await harness.GetRequiredService<IInquiry>().BeginTransactionAsync();
        var stream = tx.QueryAsync<Customer>($"SELECT CustomerID, CompanyName FROM Customers");

        if (dispose) await tx.DisposeAsync(); else await tx.RollbackAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
        {
            await foreach (var _ in stream) { }
        });
        await tx.DisposeAsync();
    }

    [Fact]
    public async Task ConcurrentRootDisposeCleansProviderResourcesExactlyOnce()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "dispose_once");
        var (inquiry, probe) = BuildInquiryWithProbe(harness.ConnectionString, FailureMode.None);
        var tx = await inquiry.BeginTransactionAsync();

        await Task.WhenAll(tx.DisposeAsync().AsTask(), tx.DisposeAsync().AsTask(), tx.DisposeAsync().AsTask());

        Assert.Equal(1, probe.RollbackCalls);
        Assert.Equal(1, probe.TransactionDisposeCalls);
        Assert.Equal(1, probe.ConnectionDisposeCalls);
    }

    [Fact]
    public async Task DisposeWaitsBehindAcceptedCommitThenCleansExactlyOnce()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "dispose_waits_commit");
        var (inquiry, probe) = BuildInquiryWithProbe(harness.ConnectionString, FailureMode.GatedCommit);
        var tx = await inquiry.BeginTransactionAsync();

        var commit = tx.CommitAsync();
        await probe.TerminalEntered.Task;
        var dispose = tx.DisposeAsync().AsTask();
        Assert.False(dispose.IsCompleted);
        probe.AllowTerminal.SetResult();
        await Task.WhenAll(commit, dispose);

        Assert.Equal(1, probe.CommitCalls);
        Assert.Equal(0, probe.RollbackCalls);
        Assert.Equal(1, probe.TransactionDisposeCalls);
        Assert.Equal(1, probe.ConnectionDisposeCalls);
    }

    [Fact]
    public async Task TransactionDisposeFailureStillDisposesConnectionAndRemainsPrimary()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "tx_dispose_failure");
        var (inquiry, probe) = BuildInquiryWithProbe(harness.ConnectionString, FailureMode.OnTransactionDispose);
        var tx = await inquiry.BeginTransactionAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () => await tx.DisposeAsync());
        Assert.Contains("transaction dispose", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, probe.TransactionDisposeCalls);
        Assert.Equal(1, probe.ConnectionDisposeCalls);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await tx.DisposeAsync());
        Assert.Equal(1, probe.TransactionDisposeCalls);
        Assert.Equal(1, probe.ConnectionDisposeCalls);
    }

    [Fact]
    public async Task ConnectionDisposeFailureIsReportedAfterTransactionCleanup()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "connection_dispose_failure");
        var (inquiry, probe) = BuildInquiryWithProbe(harness.ConnectionString, FailureMode.OnConnectionDispose);
        var tx = await inquiry.BeginTransactionAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () => await tx.DisposeAsync());
        Assert.Contains("connection dispose", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, probe.RollbackCalls);
        Assert.Equal(1, probe.TransactionDisposeCalls);
        Assert.Equal(1, probe.ConnectionDisposeCalls);
    }

    [Theory]
    [InlineData(FailureMode.OnReaderDispose)]
    [InlineData(FailureMode.OnCommandDispose)]
    public async Task StreamingCleanupFailureStillDisposesBothResourcesAndReleasesLease(FailureMode mode)
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "stream_cleanup_failure");
        var (inquiry, probe) = BuildInquiryWithProbe(harness.ConnectionString, mode);
        var tx = await inquiry.BeginTransactionAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in tx.QueryAsync<Customer>($"SELECT CustomerID, CompanyName FROM Customers")) { }
        });
        Assert.Contains(mode == FailureMode.OnReaderDispose ? "reader dispose" : "command dispose", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, probe.ReaderDisposeCalls);
        Assert.Equal(1, probe.CommandDisposeCalls);

        await tx.RollbackAsync();
        await tx.DisposeAsync();
    }

    [Theory]
    [InlineData(FailureMode.GatedCommit)]
    [InlineData(FailureMode.GatedRollback)]
    public async Task CancelledTerminalAndConcurrentDisposeShareCleanupAndIsolateExceptions(FailureMode mode)
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "cancelled_terminal_dispose");
        var (inquiry, probe) = BuildInquiryWithProbe(harness.ConnectionString, mode);
        var tx = await inquiry.BeginTransactionAsync();
        using var cts = new CancellationTokenSource();
        var terminal = mode == FailureMode.GatedCommit ? tx.CommitAsync(cts.Token) : tx.RollbackAsync(cts.Token);
        await probe.TerminalEntered.Task;
        var dispose = tx.DisposeAsync().AsTask();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => terminal);
        await dispose;
        Assert.Equal(mode == FailureMode.GatedCommit ? 1 : 0, probe.CommitCalls);
        Assert.Equal(mode == FailureMode.GatedRollback ? 2 : 1, probe.RollbackCalls);
        Assert.Equal(1, probe.TransactionDisposeCalls);
        Assert.Equal(1, probe.ConnectionDisposeCalls);
    }

    [Fact]
    public async Task FailedRollbackGetsOneCleanupAttemptAndDisposesResourcesOnce()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "rollback_cleanup_once");
        var (inquiry, probe) = BuildInquiryWithProbe(harness.ConnectionString, FailureMode.OnRollback);
        var tx = await inquiry.BeginTransactionAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => tx.RollbackAsync());
        await tx.DisposeAsync();

        Assert.Equal(2, probe.RollbackCalls); // terminal call + one best-effort cleanup attempt
        Assert.Equal(1, probe.TransactionDisposeCalls);
        Assert.Equal(1, probe.ConnectionDisposeCalls);
    }

    [Fact]
    public async Task DeferredSavepointStreamFirstEnumeratedAfterCloseFailsBeforeProviderUse()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "deferred_savepoint_closed");
        var inquiry = harness.GetRequiredService<IInquiry>();
        await using var outer = await inquiry.BeginTransactionAsync();
        var inner = await outer.BeginTransactionAsync();
        var stream = inner.QueryAsync<Customer>(
            $"SELECT CustomerID, CompanyName, ContactName, ContactTitle, Address, City, Region, PostalCode, Country, Phone, Fax FROM Customers");

        await inner.CommitAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
        {
            await foreach (var _ in stream) { }
        });
        await inner.DisposeAsync();
        await outer.RollbackAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeferredSavepointStreamFirstEnumeratedAfterRollbackOrDisposeFailsBeforeProviderUse(bool dispose)
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "deferred_savepoint_terminal");
        await using var outer = await harness.GetRequiredService<IInquiry>().BeginTransactionAsync();
        var inner = await outer.BeginTransactionAsync();
        var stream = inner.QueryAsync<Customer>($"SELECT CustomerID, CompanyName FROM Customers");

        if (dispose) await inner.DisposeAsync(); else await inner.RollbackAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
        {
            await foreach (var _ in stream) { }
        });
        await inner.DisposeAsync();
        await outer.RollbackAsync();
    }

    [Fact]
    public async Task SavepointDisposeWhileStreamingIsBusyIsRetryable()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "savepoint_busy_dispose");
        var inquiry = harness.GetRequiredService<IInquiry>();
        var store = harness.GetRequiredService<CustomerStore>();
        await store.InsertAsync(new Customer { CustomerID = "SP001", CompanyName = "Seed" });

        await using var outer = await inquiry.BeginTransactionAsync();
        var inner = await outer.BeginTransactionAsync();
        var enumerator = inner.QueryAsync<Customer>(
            $"SELECT CustomerID, CompanyName, ContactName, ContactTitle, Address, City, Region, PostalCode, Country, Phone, Fax FROM Customers").GetAsyncEnumerator();
        Assert.True(await enumerator.MoveNextAsync());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => await inner.DisposeAsync());
        Assert.Contains("in flight", ex.Message, StringComparison.OrdinalIgnoreCase);
        await enumerator.DisposeAsync();
        await inner.DisposeAsync();
        await outer.CommitAsync();
    }

    [Fact]
    public async Task SavepointConcurrentCommitAndRollbackHaveOneTerminalOwner()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "savepoint_terminal_race");
        var inquiry = harness.GetRequiredService<IInquiry>();
        await using var outer = await inquiry.BeginTransactionAsync();
        var inner = await outer.BeginTransactionAsync();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<Exception?> Run(Func<Task> terminal)
        {
            await gate.Task;
            try { await terminal(); return null; } catch (Exception exception) { return exception; }
        }

        var commit = Run(() => inner.CommitAsync());
        var rollback = Run(() => inner.RollbackAsync());
        gate.SetResult();
        var outcomes = await Task.WhenAll(commit, rollback);

        Assert.Single(outcomes, static e => e is null);
        Assert.Single(outcomes, static e => e is ObjectDisposedException);
        await inner.DisposeAsync();
        await outer.RollbackAsync();
    }

    [Fact]
    public async Task ConcurrentSavepointDisposeCallsShareCleanup()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "savepoint_dispose_race");
        var inquiry = harness.GetRequiredService<IInquiry>();
        await using var outer = await inquiry.BeginTransactionAsync();
        var inner = await outer.BeginTransactionAsync();

        await Task.WhenAll(inner.DisposeAsync().AsTask(), inner.DisposeAsync().AsTask());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => inner.CommitAsync());
        await outer.RollbackAsync();
    }

    [Fact]
    public async Task CommitWhileStreamingReaderIsInFlightThrowsWithoutClosingTransaction()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "commit_in_flight");
        var inquiry = harness.GetRequiredService<IInquiry>();
        var store = harness.GetRequiredService<CustomerStore>();

        await store.InsertAsync(new Customer { CustomerID = "SEED1", CompanyName = "Seed 1" });
        await store.InsertAsync(new Customer { CustomerID = "SEED2", CompanyName = "Seed 2" });

        await using var tx = await inquiry.BeginTransactionAsync();
        var streaming = tx.QueryAsync<Customer>(
            $"SELECT CustomerID, CompanyName, ContactName, ContactTitle, Address, City, Region, PostalCode, Country, Phone, Fax FROM Customers");
        var enumerator = streaming.GetAsyncEnumerator();

        try
        {
            Assert.True(await enumerator.MoveNextAsync());

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => tx.CommitAsync());
            Assert.Contains("in flight", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await enumerator.DisposeAsync();
        }

        await tx.ExecuteAsync(InsertCustomer("CMT01", "Committed", "USA"));
        await tx.CommitAsync();

        Assert.NotNull(await store.SelectByKeyAsync("CMT01"));
    }

    [Fact]
    public async Task RollbackWhileStreamingReaderIsInFlightThrowsWithoutClosingTransaction()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "rollback_in_flight");
        var inquiry = harness.GetRequiredService<IInquiry>();
        var store = harness.GetRequiredService<CustomerStore>();

        await store.InsertAsync(new Customer { CustomerID = "SEED1", CompanyName = "Seed 1" });
        await store.InsertAsync(new Customer { CustomerID = "SEED2", CompanyName = "Seed 2" });

        await using var tx = await inquiry.BeginTransactionAsync();
        await tx.ExecuteAsync(InsertCustomer("RBK01", "Rollback", "USA"));
        var streaming = tx.QueryAsync<Customer>(
            $"SELECT CustomerID, CompanyName, ContactName, ContactTitle, Address, City, Region, PostalCode, Country, Phone, Fax FROM Customers");
        var enumerator = streaming.GetAsyncEnumerator();

        try
        {
            Assert.True(await enumerator.MoveNextAsync());

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => tx.RollbackAsync());
            Assert.Contains("in flight", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await enumerator.DisposeAsync();
        }

        await tx.RollbackAsync();

        Assert.Null(await store.SelectByKeyAsync("RBK01"));
    }

    [Fact]
    public async Task StreamingQueryThatThrowsCreatingTheCommandReleasesTheInFlightSlot()
    {
        // #46. The transacted streaming QueryAsync overloads created their command BEFORE the try whose
        // finally releases the in-flight slot. If CreateCommand()/InitializeCommand throws, the slot
        // leaked and every later Commit/Rollback failed with the in-flight guard — the transaction
        // became permanently un-committable. The command must be created inside the guarded try.
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "inflight_leak");
        var inquiry = BuildInquiryWithThrowingInitializeCommand(harness.ConnectionString);

        await using var tx = await inquiry.BeginTransactionAsync();

        // The streaming overload creates its command lazily at the first MoveNextAsync, where
        // InitializeCommand throws (a faulted async iterator, before the try is entered pre-fix).
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in tx.QueryAsync<Customer>(
                $"SELECT CustomerID, CompanyName, ContactName, ContactTitle, Address, City, Region, PostalCode, Country, Phone, Fax FROM Customers"))
            {
            }
        });
        Assert.Contains("Simulated InitializeCommand failure", ex.Message);
        Assert.DoesNotContain("in flight", ex.Message, StringComparison.OrdinalIgnoreCase);

        // The in-flight slot must have been released in the finally — Commit must NOT throw 'in flight'.
        await tx.CommitAsync();
    }

    [Fact]
    public async Task CommitAndRollbackAfterRootTransactionCloseThrowObjectDisposed()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "double_close");
        var inquiry = harness.GetRequiredService<IInquiry>();

        await using (var committed = await inquiry.BeginTransactionAsync())
        {
            await committed.CommitAsync();

            await Assert.ThrowsAsync<ObjectDisposedException>(() => committed.CommitAsync());
            await Assert.ThrowsAsync<ObjectDisposedException>(() => committed.RollbackAsync());
        }

        await using (var rolledBack = await inquiry.BeginTransactionAsync())
        {
            await rolledBack.RollbackAsync();

            await Assert.ThrowsAsync<ObjectDisposedException>(() => rolledBack.CommitAsync());
            await Assert.ThrowsAsync<ObjectDisposedException>(() => rolledBack.RollbackAsync());
        }
    }

    [Fact]
    public async Task AlreadyCancelledTokenInBeginTransactionFailsCleanlyAndLeavesSlotRecoverable()
    {
        // Item #8. A cancelled BeginTransactionAsync must throw OperationCanceledException
        // and clean up the ambient slot so a subsequent BeginTransactionAsync on the same
        // async flow opens a fresh transaction (not a half-poisoned savepoint).
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "cancelled_token");
        var inquiry = harness.GetRequiredService<IInquiry>();
        var store = harness.GetRequiredService<CustomerStore>();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => inquiry.BeginTransactionAsync(cancellationToken: cts.Token));

        // The next BeginTransactionAsync (without a cancelled token) must work cleanly.
        await using var tx = await inquiry.BeginTransactionAsync();
        await tx.ExecuteAsync(InsertCustomer("RECV1", "Recovered", "USA"));
        await tx.CommitAsync();

        Assert.NotNull(await store.SelectByKeyAsync("RECV1"));
    }

    [Fact]
    public async Task PreparedStatementsInsideTransactionDoNotCrashTheTransactedPipeline()
    {
        // Item #10. When PreparedStatementMode.Auto is on, the transacted pipeline calls
        // DbCommand.PrepareAsync before each non-StoredProcedure command. SQLite's
        // PrepareAsync is a no-op (no persistent plan cache), so this is purely a
        // "doesn't crash" verification on SQLite. Provider-specific behavior (Npgsql's
        // server-side prepared-statement cache) is exercised by the networked dialect
        // test projects' own prepared-statement coverage.
        var connStringBuilder = new SqliteConnectionStringBuilder
        {
            DataSource = "Inquiry_prep_" + Guid.NewGuid().ToString("N"),
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared,
        };
        var connectionString = connStringBuilder.ToString();

        // Keep the in-memory database alive for the duration of the test.
        await using var keeper = new SqliteConnection(connectionString);
        await keeper.OpenAsync();
        await using (var schemaCmd = keeper.CreateCommand())
        {
            schemaCmd.CommandText = NorthwindSchema.SqliteDdl;
            await schemaCmd.ExecuteNonQueryAsync();
        }

        // Bespoke service-collection wiring so we can pass InquiryOptions.
        var services = new ServiceCollection()
            .AddInquiry(opt => opt.PrepareStatements = PreparedStatementMode.Auto, typeof(CustomerStore).Assembly)
            .AddInquirySqlite(connectionString)
            .BuildServiceProvider();

        var inquiry = services.GetRequiredService<IInquiry>();

        await using (var tx = await inquiry.BeginTransactionAsync())
        {
            // Multiple prepared statements inside one transaction — exercises the
            // MaybePrepareAsync code path for each.
            await tx.ExecuteAsync(InsertCustomer("PRE01", "Prepared 1", "USA"));
            await tx.ExecuteAsync(InsertCustomer("PRE02", "Prepared 2", "USA"));
            var single = await tx.QuerySingleOrDefaultAsync<Customer>(
                $"SELECT CustomerID, CompanyName, ContactName, ContactTitle, Address, City, Region, PostalCode, Country, Phone, Fax FROM Customers WHERE CustomerID = 'PRE01'");
            Assert.NotNull(single);

            await tx.CommitAsync();
        }

        await services.DisposeAsync();
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

    private static (IInquiry Inquiry, LifecycleProbe Probe) BuildInquiryWithProbe(string connectionString, FailureMode failureMode)
    {
        var probe = new LifecycleProbe();
        var services = new ServiceCollection().AddInquiry(typeof(CustomerStore).Assembly).AddInquirySqlite(connectionString);
        services.RemoveAll(typeof(IInquiryConnectionFactory));
        services.AddSingleton<IInquiryConnectionFactory>(new FailingConnectionFactory(connectionString, failureMode, probe));
        return (services.BuildServiceProvider().GetRequiredService<IInquiry>(), probe);
    }

    /// <summary>
    /// Builds an IInquiry whose connection factory throws from <see cref="IInquiryConnectionFactory.InitializeCommand"/>,
    /// so every <c>CreateCommand()</c> in the pipeline faults. Reuses the harness's shared in-memory database.
    /// </summary>
    private static IInquiry BuildInquiryWithThrowingInitializeCommand(string connectionString)
    {
        var services = new ServiceCollection()
            .AddInquiry(typeof(CustomerStore).Assembly)
            .AddInquirySqlite(connectionString);

        services.RemoveAll(typeof(IInquiryConnectionFactory));
        services.AddSingleton<IInquiryConnectionFactory>(new ThrowingInitializeCommandFactory(connectionString));

        return services.BuildServiceProvider().GetRequiredService<IInquiry>();
    }

    private sealed class ThrowingInitializeCommandFactory : IInquiryConnectionFactory
    {
        private readonly string _connectionString;
        public ThrowingInitializeCommandFactory(string connectionString) => _connectionString = connectionString;

        public async ValueTask<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
        {
            var inner = new SqliteConnection(_connectionString);
            await inner.OpenAsync(cancellationToken);
            return inner;
        }

        public void InitializeCommand(DbCommand command)
            => throw new InvalidOperationException("Simulated InitializeCommand failure for tests.");
    }

    public enum FailureMode
    {
        None,
        OnCommit,
        OnRollback,
        OnTransactionDispose,
        OnConnectionDispose,
        OnReaderDispose,
        OnCommandDispose,
        GatedCommit,
        GatedRollback,
    }

    private sealed class LifecycleProbe
    {
        public int CommitCalls;
        public int RollbackCalls;
        public int TransactionDisposeCalls;
        public int ConnectionDisposeCalls;
        public int ReaderDisposeCalls;
        public int CommandDisposeCalls;
        public TaskCompletionSource TerminalEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AllowTerminal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class FailingConnectionFactory : IInquiryConnectionFactory
    {
        private readonly string _connectionString;
        private readonly FailureMode _failureMode;
        private readonly LifecycleProbe _probe;

        public FailingConnectionFactory(string connectionString, FailureMode failureMode, LifecycleProbe? probe = null)
        {
            _connectionString = connectionString;
            _failureMode = failureMode;
            _probe = probe ?? new LifecycleProbe();
        }

        public async ValueTask<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
        {
            var inner = new SqliteConnection(_connectionString);
            await inner.OpenAsync(cancellationToken);
            return new FailingConnection(inner, _failureMode, _probe);
        }
    }

    /// <summary>Forwards everything to the inner SqliteConnection except BeginDbTransaction (returns the failing wrapper) and CreateDbCommand (returns a wrapper that unwraps the failing tx when assigning Transaction).</summary>
    private sealed class FailingConnection : DbConnection
    {
        private readonly SqliteConnection _inner;
        private readonly FailureMode _failureMode;
        private readonly LifecycleProbe _probe;

        public FailingConnection(SqliteConnection inner, FailureMode failureMode, LifecycleProbe probe)
        {
            _inner = inner;
            _failureMode = failureMode;
            _probe = probe;
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
            => new FailingTransaction(_inner.BeginTransaction(isolationLevel), _failureMode, _probe);

        protected override DbCommand CreateDbCommand()
            => new FailingCommand(_inner.CreateCommand(), _failureMode, _probe);

        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            System.Threading.Interlocked.Increment(ref _probe.ConnectionDisposeCalls);
            await _inner.DisposeAsync();
            if (_failureMode == FailureMode.OnConnectionDispose) throw new InvalidOperationException("Simulated connection dispose failure.");
        }
    }

    /// <summary>Wraps SqliteCommand. Forwards everything to the inner — the Transaction setter unwraps a FailingTransaction to assign the real underlying SqliteTransaction (which is what the SqliteCommand requires).</summary>
    private sealed class FailingCommand : DbCommand
    {
        private readonly SqliteCommand _inner;
        private readonly FailureMode _failureMode;
        private readonly LifecycleProbe _probe;
        public FailingCommand(SqliteCommand inner, FailureMode failureMode, LifecycleProbe probe)
            => (_inner, _failureMode, _probe) = (inner, failureMode, probe);

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
            => new FailingReader(await _inner.ExecuteReaderAsync(behavior, ct), _failureMode, _probe);

        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            System.Threading.Interlocked.Increment(ref _probe.CommandDisposeCalls);
            await _inner.DisposeAsync();
            if (_failureMode == FailureMode.OnCommandDispose) throw new InvalidOperationException("Simulated command dispose failure.");
        }

    }

    private sealed class FailingReader : DbDataReader
    {
        private readonly DbDataReader _inner;
        private readonly FailureMode _failureMode;
        private readonly LifecycleProbe _probe;
        public FailingReader(DbDataReader inner, FailureMode failureMode, LifecycleProbe probe)
            => (_inner, _failureMode, _probe) = (inner, failureMode, probe);
        public override object this[int ordinal] => _inner[ordinal];
        public override object this[string name] => _inner[name];
        public override int Depth => _inner.Depth;
        public override int FieldCount => _inner.FieldCount;
        public override bool HasRows => _inner.HasRows;
        public override bool IsClosed => _inner.IsClosed;
        public override int RecordsAffected => _inner.RecordsAffected;
        public override bool GetBoolean(int ordinal) => _inner.GetBoolean(ordinal);
        public override byte GetByte(int ordinal) => _inner.GetByte(ordinal);
        public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => _inner.GetBytes(ordinal, dataOffset, buffer, bufferOffset, length);
        public override char GetChar(int ordinal) => _inner.GetChar(ordinal);
        public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => _inner.GetChars(ordinal, dataOffset, buffer, bufferOffset, length);
        public override string GetDataTypeName(int ordinal) => _inner.GetDataTypeName(ordinal);
        public override DateTime GetDateTime(int ordinal) => _inner.GetDateTime(ordinal);
        public override decimal GetDecimal(int ordinal) => _inner.GetDecimal(ordinal);
        public override double GetDouble(int ordinal) => _inner.GetDouble(ordinal);
        public override Type GetFieldType(int ordinal) => _inner.GetFieldType(ordinal);
        public override float GetFloat(int ordinal) => _inner.GetFloat(ordinal);
        public override Guid GetGuid(int ordinal) => _inner.GetGuid(ordinal);
        public override short GetInt16(int ordinal) => _inner.GetInt16(ordinal);
        public override int GetInt32(int ordinal) => _inner.GetInt32(ordinal);
        public override long GetInt64(int ordinal) => _inner.GetInt64(ordinal);
        public override string GetName(int ordinal) => _inner.GetName(ordinal);
        public override int GetOrdinal(string name) => _inner.GetOrdinal(name);
        public override string GetString(int ordinal) => _inner.GetString(ordinal);
        public override object GetValue(int ordinal) => _inner.GetValue(ordinal);
        public override int GetValues(object[] values) => _inner.GetValues(values);
        public override bool IsDBNull(int ordinal) => _inner.IsDBNull(ordinal);
        public override bool NextResult() => _inner.NextResult();
        public override bool Read() => _inner.Read();
        public override System.Collections.IEnumerator GetEnumerator() => ((System.Collections.IEnumerable)_inner).GetEnumerator();
        public override Task<bool> ReadAsync(CancellationToken cancellationToken) => _inner.ReadAsync(cancellationToken);
        public override Task<bool> NextResultAsync(CancellationToken cancellationToken) => _inner.NextResultAsync(cancellationToken);
        public override async ValueTask DisposeAsync()
        {
            System.Threading.Interlocked.Increment(ref _probe.ReaderDisposeCalls);
            await _inner.DisposeAsync();
            if (_failureMode == FailureMode.OnReaderDispose) throw new InvalidOperationException("Simulated reader dispose failure.");
        }
    }

    /// <summary>Wraps a real SqliteTransaction. Throws on Commit or Rollback based on FailureMode.</summary>
    private sealed class FailingTransaction : DbTransaction
    {
        internal DbTransaction Inner { get; }
        private readonly FailureMode _failureMode;
        private readonly LifecycleProbe _probe;

        public FailingTransaction(DbTransaction inner, FailureMode failureMode, LifecycleProbe probe)
        {
            Inner = inner;
            _failureMode = failureMode;
            _probe = probe;
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
            System.Threading.Interlocked.Increment(ref _probe.CommitCalls);
            if (_failureMode == FailureMode.GatedCommit)
            {
                _probe.TerminalEntered.TrySetResult();
                await _probe.AllowTerminal.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
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
            var rollbackCall = System.Threading.Interlocked.Increment(ref _probe.RollbackCalls);
            if (_failureMode == FailureMode.GatedRollback && rollbackCall == 1)
            {
                _probe.TerminalEntered.TrySetResult();
                await _probe.AllowTerminal.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
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

        public override async ValueTask DisposeAsync()
        {
            System.Threading.Interlocked.Increment(ref _probe.TransactionDisposeCalls);
            await Inner.DisposeAsync();
            if (_failureMode == FailureMode.OnTransactionDispose) throw new InvalidOperationException("Simulated transaction dispose failure.");
        }
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
