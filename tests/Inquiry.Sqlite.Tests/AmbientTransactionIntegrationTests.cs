using Inquiry.Northwind;
using Inquiry.Northwind.Models;
using Inquiry.Northwind.Stores;
using Inquiry.Sqlite.Tests.Fixtures;

namespace Inquiry.Sqlite.Tests;

/// <summary>
/// Tests for the ambient-transaction mechanism on <see cref="DefaultInquiry"/>: when a
/// transaction is begun, all subsequent IInquiry calls in the same async control flow
/// (including those routed through generated stores) participate in that transaction
/// automatically.
/// </summary>
public sealed class AmbientTransactionIntegrationTests
{
    [Fact]
    public async Task StoreCallInsideTransactionIsRolledBack()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "Ambient");
        var inquiry = harness.GetRequiredService<IInquiry>();
        var store = harness.GetRequiredService<CustomerStore>();

        await using (var tx = await inquiry.BeginTransactionAsync())
        {
            await store.InsertAsync(new Customer { CustomerID = "ROLL1", CompanyName = "Rolled Back" });
            // No commit — dispose rolls back.
        }

        var loaded = await store.SelectByKeyAsync("ROLL1");
        Assert.Null(loaded);
    }

    [Fact]
    public async Task StoreCallInsideTransactionIsCommitted()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "Ambient");
        var inquiry = harness.GetRequiredService<IInquiry>();
        var store = harness.GetRequiredService<CustomerStore>();

        await using (var tx = await inquiry.BeginTransactionAsync())
        {
            await store.InsertAsync(new Customer { CustomerID = "COMM1", CompanyName = "Committed" });
            await tx.CommitAsync();
        }

        var loaded = await store.SelectByKeyAsync("COMM1");
        Assert.NotNull(loaded);
        Assert.Equal("Committed", loaded!.CompanyName);
    }

    [Fact]
    public async Task MultipleStoresInsideOneTransactionShareTheSameConnection()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "Ambient");
        var inquiry = harness.GetRequiredService<IInquiry>();
        var customers = harness.GetRequiredService<CustomerStore>();
        var categories = harness.GetRequiredService<CategoryStore>();

        await using (var tx = await inquiry.BeginTransactionAsync())
        {
            await customers.InsertAsync(new Customer { CustomerID = "ROLL2", CompanyName = "Customer" });
            await categories.InsertAsync(new Category { CategoryName = "RolledBackCat" });
            // Dispose without commit — both rolled back.
        }

        Assert.Null(await customers.SelectByKeyAsync("ROLL2"));
        var allCats = await categories.SelectAllAsync().ToListAsync();
        Assert.DoesNotContain(allCats, c => c.CategoryName == "RolledBackCat");
    }

    [Fact]
    public async Task StoreCallOutsideTransactionUsesDefaultPipeline()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "Ambient");
        var inquiry = harness.GetRequiredService<IInquiry>();
        var store = harness.GetRequiredService<CustomerStore>();

        // No transaction — should hit the default pipeline and persist immediately.
        await store.InsertAsync(new Customer { CustomerID = "DEF01", CompanyName = "Default" });

        var loaded = await store.SelectByKeyAsync("DEF01");
        Assert.NotNull(loaded);
    }

    [Fact]
    public async Task AmbientSlotIsRestoredAfterTransactionDisposes()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "Ambient");
        var inquiry = harness.GetRequiredService<IInquiry>();
        var store = harness.GetRequiredService<CustomerStore>();

        // Inner scope rolls back.
        await using (var tx = await inquiry.BeginTransactionAsync())
        {
            await store.InsertAsync(new Customer { CustomerID = "INNER", CompanyName = "Rolled" });
        }

        // After the inner tx disposes, ambient is cleared. Insert below should commit
        // to the default pipeline immediately (not rolled back).
        await store.InsertAsync(new Customer { CustomerID = "AFTER", CompanyName = "Persisted" });

        Assert.Null(await store.SelectByKeyAsync("INNER"));
        Assert.NotNull(await store.SelectByKeyAsync("AFTER"));
    }

    [Fact]
    public async Task BackToBackTransactionsEachOpenFreshPipeline()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "Ambient");
        var inquiry = harness.GetRequiredService<IInquiry>();
        var store = harness.GetRequiredService<CustomerStore>();

        await using (var tx1 = await inquiry.BeginTransactionAsync())
        {
            await store.InsertAsync(new Customer { CustomerID = "TX1", CompanyName = "First" });
            await tx1.CommitAsync();
        }

        await using (var tx2 = await inquiry.BeginTransactionAsync())
        {
            await store.InsertAsync(new Customer { CustomerID = "TX2", CompanyName = "Second" });
            await tx2.CommitAsync();
        }

        Assert.NotNull(await store.SelectByKeyAsync("TX1"));
        Assert.NotNull(await store.SelectByKeyAsync("TX2"));
    }

    [Fact]
    public async Task ParallelTransactionsOnSeparateAsyncFlowsDoNotInterfere()
    {
        // Two separate harnesses, but both run on the same shared DefaultInquiry instance pattern.
        // Each transaction runs on its own async control flow; AsyncLocal should keep them isolated.
        await using var harnessA = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "AmbientA");
        await using var harnessB = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "AmbientB");

        var taskA = Task.Run(async () =>
        {
            var inquiry = harnessA.GetRequiredService<IInquiry>();
            var store = harnessA.GetRequiredService<CustomerStore>();
            await using var tx = await inquiry.BeginTransactionAsync();
            await store.InsertAsync(new Customer { CustomerID = "A1", CompanyName = "FlowA" });
            await tx.CommitAsync();
        });

        var taskB = Task.Run(async () =>
        {
            var inquiry = harnessB.GetRequiredService<IInquiry>();
            var store = harnessB.GetRequiredService<CustomerStore>();
            await using var tx = await inquiry.BeginTransactionAsync();
            await store.InsertAsync(new Customer { CustomerID = "B1", CompanyName = "FlowB" });
            await tx.CommitAsync();
        });

        await Task.WhenAll(taskA, taskB);

        var storeA = harnessA.GetRequiredService<CustomerStore>();
        var storeB = harnessB.GetRequiredService<CustomerStore>();

        Assert.NotNull(await storeA.SelectByKeyAsync("A1"));
        Assert.Null(await storeA.SelectByKeyAsync("B1"));
        Assert.NotNull(await storeB.SelectByKeyAsync("B1"));
        Assert.Null(await storeB.SelectByKeyAsync("A1"));
    }

    [Fact]
    public async Task ConcurrentOperationsInsideOneTransactionThrow()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "Ambient");
        var inquiry = harness.GetRequiredService<IInquiry>();
        var store = harness.GetRequiredService<CustomerStore>();

        // Seed two rows OUTSIDE the transaction so the in-tx SelectAll has rows to stream.
        await store.InsertAsync(new Customer { CustomerID = "SEED1", CompanyName = "Seed 1" });
        await store.InsertAsync(new Customer { CustomerID = "SEED2", CompanyName = "Seed 2" });

        await using var tx = await inquiry.BeginTransactionAsync();

        // Read the first row but leave the iterator suspended — _inFlight stays set on the
        // transactional pipeline until the iterator is disposed. CustomerStore.SelectAllAsync
        // is buffered now, so use IInquiry.QueryAsync directly to keep the streaming shape
        // this test exercises.
        var streaming = tx.Inquiry.QueryAsync<Customer>(
            "SELECT CustomerID, CompanyName, ContactName, ContactTitle, Address, City, Region, PostalCode, Country, Phone, Fax FROM Customers");
        var enumerator = streaming.GetAsyncEnumerator();
        try
        {
            var hasFirst = await enumerator.MoveNextAsync();
            Assert.True(hasFirst);

            // A second op on the same transaction must throw rather than corrupt the connection.
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => store.InsertAsync(new Customer { CustomerID = "CONC1", CompanyName = "Concurrent" }));
            Assert.Contains("in flight", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await enumerator.DisposeAsync();
        }
    }

    [Fact]
    public async Task TxInquiryRoutesThroughTheAmbientTransactionalPipeline()
    {
        // Previously this test asserted Assert.Same(inquiry, tx.Inquiry) — but that
        // implementation detail was the root cause of the silent-autocommit-after-close
        // bug (tx.Inquiry held the root singleton, which had no idea the tx had closed).
        // tx.Inquiry is now a transaction-scoped wrapper. The CONTRACT it preserves is
        // behavioral: operations through tx.Inquiry run inside this transaction. Verify
        // that contract directly — an insert via tx.Inquiry inside the tx must be
        // visible to a subsequent select via tx.Inquiry inside the same tx, and must be
        // rolled back when the tx disposes without committing.
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "Ambient");
        var inquiry = harness.GetRequiredService<IInquiry>();
        var store = harness.GetRequiredService<CustomerStore>();

        await using (var tx = await inquiry.BeginTransactionAsync())
        {
            await tx.Inquiry.ExecuteAsync(
                "INSERT INTO Customers (CustomerID, CompanyName, Country) VALUES (@CustomerID, @CompanyName, @Country)",
                new { CustomerID = "SCOPED", CompanyName = "Via tx.Inquiry", Country = "USA" });

            // Inside the tx, the same handle observes the insert.
            var inTx = await tx.Inquiry.QuerySingleOrDefaultAsync<Customer>(
                "SELECT CustomerID, CompanyName, ContactName, ContactTitle, Address, City, Region, PostalCode, Country, Phone, Fax FROM Customers WHERE CustomerID = 'SCOPED'");
            Assert.NotNull(inTx);

            // dispose without commit → rollback
        }

        // Outside the tx, the insert is gone — proving tx.Inquiry routed through the tx.
        Assert.Null(await store.SelectByKeyAsync("SCOPED"));
    }

    [Fact]
    public async Task TxInquiryAfterCommitThrowsObjectDisposed()
    {
        // The core P1 fix: tx.Inquiry.X(...) after tx closes must fail-fast rather than
        // silently routing through the non-transactional default pipeline.
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "Ambient");
        var inquiry = harness.GetRequiredService<IInquiry>();

        var tx = await inquiry.BeginTransactionAsync();
        await tx.CommitAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => tx.Inquiry.ExecuteAsync(
                "INSERT INTO Customers (CustomerID, CompanyName, Country) VALUES (@CustomerID, @CompanyName, @Country)",
                new { CustomerID = "AFTC2", CompanyName = "AfterCommit", Country = "USA" }));
    }

    [Fact]
    public async Task TxInquiryAfterRollbackThrowsObjectDisposed()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "Ambient");
        var inquiry = harness.GetRequiredService<IInquiry>();

        var tx = await inquiry.BeginTransactionAsync();
        await tx.RollbackAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => tx.Inquiry.QueryListAsync<Customer>(
                "SELECT CustomerID, CompanyName, ContactName, ContactTitle, Address, City, Region, PostalCode, Country, Phone, Fax FROM Customers"));
    }

    [Fact]
    public async Task TxInquiryAfterDisposeThrowsObjectDisposed()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "Ambient");
        var inquiry = harness.GetRequiredService<IInquiry>();

        var tx = await inquiry.BeginTransactionAsync();
        await tx.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => tx.Inquiry.ExecuteAsync(
                "INSERT INTO Customers (CustomerID, CompanyName, Country) VALUES (@CustomerID, @CompanyName, @Country)",
                new { CustomerID = "AFTD2", CompanyName = "AfterDispose", Country = "USA" }));
    }

    [Fact]
    public async Task SavepointTxInquiryAfterCommitThrowsObjectDisposed()
    {
        // Same fail-fast contract for the savepoint case — savepointTx.Inquiry.X(...)
        // after the savepoint is released must throw.
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "Ambient");
        var inquiry = harness.GetRequiredService<IInquiry>();

        await using var outer = await inquiry.BeginTransactionAsync();
        var inner = await outer.BeginTransactionAsync();
        await inner.CommitAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => inner.Inquiry.ExecuteAsync(
                "INSERT INTO Customers (CustomerID, CompanyName, Country) VALUES (@CustomerID, @CompanyName, @Country)",
                new { CustomerID = "SPINQ", CompanyName = "SavepointInquiryAfterCommit", Country = "USA" }));

        await outer.CommitAsync();
    }
}
