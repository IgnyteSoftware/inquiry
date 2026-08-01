using Inquiry.Northwind.Models;
using Inquiry.Northwind.Stores;
using Inquiry.Oracle.Tests.Fixtures;
using Inquiry.Stores;
using System.Data;

namespace Inquiry.Oracle.Tests;

/// <summary>
/// Mirror of <c>Inquiry.Sqlite.Tests.TransactionStateMachineTests</c> for Oracle.
/// Two coverage cuts ported to the live engine:
///   1. Concurrent transactions on the same DI scope must isolate via AsyncLocal even when
///      sharing one <see cref="DefaultInquiry"/> instance (the SQLite test uses two harnesses;
///      this one runs both flows against one harness).
///   4. The documented pattern where a class deriving from <see cref="InquiryStore{TEntity}"/>
///      opens its own transaction via the inherited <c>Inquiry</c> property and other store
///      calls join automatically.
/// </summary>
/// <remarks>
/// Item 2 (failed Commit/Rollback state machine) is intentionally NOT ported. The state-machine
/// fix lives entirely in <see cref="Inquiry.Transactions.InquiryTransactionBase"/> /
/// <see cref="Inquiry.Transactions.InquiryTransaction"/> / <see cref="Inquiry.Transactions.SavepointInquiryTransaction"/>
/// — provider-agnostic code. SQLite coverage with the failing-tx wrapper is sufficient; a real
/// Oracle wrapper would duplicate ~150 lines of Oracle.ManagedDataAccess-specific plumbing to test the same
/// provider-agnostic code path.
/// </remarks>
[Collection(OracleCollection.Name)]
public sealed class TransactionStateMachineTests
{
    private readonly OracleContainerFixture _fixture;
    public TransactionStateMachineTests(OracleContainerFixture fixture) => _fixture = fixture;

    // ---- Concurrent transactions on the SAME DI scope (item 1) -----------------------

    [SkippableFact]
    public async Task ConcurrentTransactionsOnSameDIScopeBothCommit()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "tx_concurrent_both");
        var inquiry = harness.GetRequiredService<IInquiry>();
        var store = harness.GetRequiredService<CustomerStore>();

        var t1 = Task.Run(async () =>
        {
            await using var tx = await inquiry.BeginTransactionAsync();
            await store.InsertAsync(new Customer { CustomerID = "PAR01", CompanyName = "Parallel 1" });
            await tx.CommitAsync();
        });
        var t2 = Task.Run(async () =>
        {
            await using var tx = await inquiry.BeginTransactionAsync();
            await store.InsertAsync(new Customer { CustomerID = "PAR02", CompanyName = "Parallel 2" });
            await tx.CommitAsync();
        });

        await Task.WhenAll(t1, t2);

        Assert.NotNull(await store.SelectByKeyAsync("PAR01"));
        Assert.NotNull(await store.SelectByKeyAsync("PAR02"));
    }

    [SkippableFact]
    public async Task ConcurrentTransactionsOnSameDIScopeOneCommitsOneRollsBack()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "tx_concurrent_mixed");
        var inquiry = harness.GetRequiredService<IInquiry>();
        var store = harness.GetRequiredService<CustomerStore>();

        var committed = Task.Run(async () =>
        {
            await using var tx = await inquiry.BeginTransactionAsync();
            await store.InsertAsync(new Customer { CustomerID = "KEEP1", CompanyName = "Keep" });
            await tx.CommitAsync();
        });
        var rolled = Task.Run(async () =>
        {
            await using var tx = await inquiry.BeginTransactionAsync();
            await store.InsertAsync(new Customer { CustomerID = "GONE1", CompanyName = "Gone" });
            await tx.RollbackAsync();
        });

        await Task.WhenAll(committed, rolled);

        Assert.NotNull(await store.SelectByKeyAsync("KEEP1"));
        Assert.Null(await store.SelectByKeyAsync("GONE1"));
    }

    [SkippableFact]
    public async Task ManyConcurrentTransactionsAllCommitIndependently()
    {
        // 16-way Task.WhenAll against the same DefaultInquiry. Exercises real provider
        // connection-pool contention plus AsyncLocal slot isolation under load.
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "tx_concurrent_many");
        var inquiry = harness.GetRequiredService<IInquiry>();
        var store = harness.GetRequiredService<CustomerStore>();

        const int N = 16;
        var tasks = Enumerable.Range(0, N).Select(i => Task.Run(async () =>
        {
            await using var tx = await inquiry.BeginTransactionAsync();
            await store.InsertAsync(new Customer { CustomerID = "C" + i.ToString("D4"), CompanyName = "Concurrent " + i });
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

    // ---- Custom InquiryStore-derived method opens its own transaction (item 4) -------

    [SkippableFact]
    public async Task CustomStoreMethodOpensTransactionAndOtherStoreCallsJoinIt()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "tx_custom_commit");
        var inquiry = harness.GetRequiredService<IInquiry>();
        var customers = harness.GetRequiredService<CustomerStore>();
        var atomic = new CustomerAtomicService(inquiry, customers);

        await atomic.InsertManyAtomicallyAsync(new[]
        {
            new Customer { CustomerID = "ATM01", CompanyName = "Atomic 1" },
            new Customer { CustomerID = "ATM02", CompanyName = "Atomic 2" },
            new Customer { CustomerID = "ATM03", CompanyName = "Atomic 3" },
        }, commit: true);

        Assert.NotNull(await customers.SelectByKeyAsync("ATM01"));
        Assert.NotNull(await customers.SelectByKeyAsync("ATM02"));
        Assert.NotNull(await customers.SelectByKeyAsync("ATM03"));
    }

    [SkippableFact]
    public async Task CustomStoreMethodTransactionRollbackRevertsOtherStoreCalls()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "tx_custom_rollback");
        var inquiry = harness.GetRequiredService<IInquiry>();
        var customers = harness.GetRequiredService<CustomerStore>();
        var atomic = new CustomerAtomicService(inquiry, customers);

        await atomic.InsertManyAtomicallyAsync(new[]
        {
            new Customer { CustomerID = "RBK01", CompanyName = "Reverted 1" },
            new Customer { CustomerID = "RBK02", CompanyName = "Reverted 2" },
        }, commit: false);

        Assert.Null(await customers.SelectByKeyAsync("RBK01"));
        Assert.Null(await customers.SelectByKeyAsync("RBK02"));
    }

    // ---- IsolationLevel round-trip on the real provider (item #9) --------------------

    [SkippableFact]
    public async Task IsolationLevelRoundTripsToHandleProperty()
    {
        // Verifies the requested IsolationLevel flows through DbConnection.BeginTransactionAsync
        // and is observable on tx.IsolationLevel. Oracle.ManagedDataAccess supports
        // ReadCommitted and Serializable; Serializable round-trips cleanly.
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "tx_iso");
        var inquiry = harness.GetRequiredService<IInquiry>();

        await using var tx = await inquiry.BeginTransactionAsync(IsolationLevel.Serializable);
        Assert.Equal(IsolationLevel.Serializable, tx.IsolationLevel);
    }
}

/// <summary>
/// Derived InquiryStore using the inherited Inquiry property to open a transaction inside a
/// hand-written method, then calling another injected store. Documents the pattern the
/// transactions feature page describes, ported to the Oracle live engine.
/// </summary>
internal sealed partial class CustomerAtomicService : InquiryStore<Customer>
{
    private readonly CustomerStore _customers;

    public CustomerAtomicService(IInquiry inquiry, CustomerStore customers) : base(inquiry)
        => _customers = customers;

    public async Task InsertManyAtomicallyAsync(IReadOnlyList<Customer> customers, bool commit, CancellationToken cancellationToken = default)
    {
        await using var tx = await Inquiry.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        foreach (var c in customers)
        {
            await _customers.InsertAsync(c, cancellationToken);
        }
        if (commit) await tx.CommitAsync(cancellationToken);
    }
}
