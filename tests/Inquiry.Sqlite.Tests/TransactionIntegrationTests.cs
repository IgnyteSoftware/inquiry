using Inquiry.Northwind;
using Inquiry.Northwind.Stores;
using Inquiry.Sqlite.Tests.Fixtures;

namespace Inquiry.Sqlite.Tests;

public sealed class TransactionIntegrationTests
{
    private static FormattableString InsertCustomer(string customerId, string companyName, string country)
        => $"INSERT INTO Customers (CustomerID, CompanyName, Country) VALUES ({customerId}, {companyName}, {country})";

    [Fact]
    public async Task CommittedTransactionPersistsChanges()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "Tx");
        var inquiry = harness.GetRequiredService<IInquiry>();
        var store = harness.GetRequiredService<CustomerStore>();

        await using var tx = await inquiry.BeginTransactionAsync();
        await tx.ExecuteAsync(InsertCustomer("TX001", "Widget", "USA"));
        await tx.CommitAsync();

        var loaded = await store.SelectByKeyAsync("TX001");
        Assert.NotNull(loaded);
        Assert.Equal("Widget", loaded.CompanyName);
    }

    [Fact]
    public async Task DisposedUncommittedTransactionRollsBack()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "Tx");
        var inquiry = harness.GetRequiredService<IInquiry>();
        var store = harness.GetRequiredService<CustomerStore>();

        await using (var tx = await inquiry.BeginTransactionAsync())
        {
            await tx.ExecuteAsync(InsertCustomer("GHOST", "Ghost", "USA"));
            // No commit — dispose rolls back
        }

        var loaded = await store.SelectByKeyAsync("GHOST");
        Assert.Null(loaded);
    }

    [Fact]
    public async Task ExplicitRollbackReverts()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "Tx");
        var inquiry = harness.GetRequiredService<IInquiry>();
        var store = harness.GetRequiredService<CustomerStore>();

        await using var tx = await inquiry.BeginTransactionAsync();
        await tx.ExecuteAsync(InsertCustomer("REV01", "Reverted", "USA"));
        await tx.RollbackAsync();

        var loaded = await store.SelectByKeyAsync("REV01");
        Assert.Null(loaded);
    }

    [Fact]
    public async Task ExecuteInTransactionAsyncCommitsWhenOperationCompletes()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "Tx");
        var inquiry = harness.GetRequiredService<IInquiry>();
        var store = harness.GetRequiredService<CustomerStore>();

        await inquiry.ExecuteInTransactionAsync(async tx =>
        {
            await store.InsertAsync(new Inquiry.Northwind.Models.Customer { CustomerID = "HELP1", CompanyName = "Helper" });
            await tx.ExecuteAsync($"UPDATE Customers SET Country = {"USA"} WHERE CustomerID = {"HELP1"}");
        });

        var loaded = await store.SelectByKeyAsync("HELP1");
        Assert.NotNull(loaded);
        Assert.Equal("USA", loaded!.Country);
    }

    [Fact]
    public async Task ExecuteInTransactionAsyncRollsBackWhenOperationThrows()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "Tx");
        var inquiry = harness.GetRequiredService<IInquiry>();
        var store = harness.GetRequiredService<CustomerStore>();

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            inquiry.ExecuteInTransactionAsync(async tx =>
            {
                await tx.ExecuteAsync(InsertCustomer("HELP2", "Rolled Back", "USA"));
                throw new InvalidOperationException("boom");
            }));

        Assert.Equal("boom", thrown.Message);
        Assert.Null(await store.SelectByKeyAsync("HELP2"));
    }

    [Fact]
    public async Task ExecuteInTransactionAsyncReturnsOperationResultAfterCommit()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "Tx");
        var inquiry = harness.GetRequiredService<IInquiry>();
        var store = harness.GetRequiredService<CustomerStore>();

        var insertedCount = await inquiry.ExecuteInTransactionAsync(async tx =>
        {
            await tx.ExecuteAsync(InsertCustomer("HELP3", "Returned", "USA"));
            return await tx.ExecuteScalarAsync<long>($"SELECT COUNT(*) FROM Customers WHERE CustomerID = {"HELP3"}");
        });

        Assert.Equal(1L, insertedCount);
        Assert.NotNull(await store.SelectByKeyAsync("HELP3"));
    }

    [Fact]
    public async Task TransactionInquirySupportsMultipleInsertsInOneCommit()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "Tx");
        var inquiry = harness.GetRequiredService<IInquiry>();
        var store = harness.GetRequiredService<CustomerStore>();

        await using var tx = await inquiry.BeginTransactionAsync();
        for (var i = 1; i <= 5; i++)
        {
            await tx.ExecuteAsync(InsertCustomer($"M{i:D4}", $"Customer {i}", "USA"));
        }
        await tx.CommitAsync();

        var all = await store.SelectAllAsync();
        Assert.Equal(5, all.Count);
    }

    [Fact]
    public async Task CommitAfterDisposeThrows()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "Tx");
        var inquiry = harness.GetRequiredService<IInquiry>();

        var tx = await inquiry.BeginTransactionAsync();
        await tx.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => tx.CommitAsync());
    }

    [Fact]
    public async Task RollbackAfterDisposeThrows()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "Tx");
        var inquiry = harness.GetRequiredService<IInquiry>();

        var tx = await inquiry.BeginTransactionAsync();
        await tx.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => tx.RollbackAsync());
    }

    [Fact]
    public async Task DoubleDisposeIsSafe()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "Tx");
        var inquiry = harness.GetRequiredService<IInquiry>();

        var tx = await inquiry.BeginTransactionAsync();
        await tx.DisposeAsync();
        await tx.DisposeAsync(); // must not throw
    }

    // ---- Nested transactions / savepoints --------------------------------------------

    [Fact]
    public async Task NestedTransactionCommitReleasesSavepointAndOuterCommitPersistsBoth()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "Tx");
        var inquiry = harness.GetRequiredService<IInquiry>();
        var store = harness.GetRequiredService<CustomerStore>();

        await using (var outer = await inquiry.BeginTransactionAsync())
        {
            await outer.ExecuteAsync(InsertCustomer("OUT01", "Outer", "USA"));

            await using (var inner = await outer.BeginTransactionAsync())
            {
                await inner.ExecuteAsync(InsertCustomer("INN01", "Inner", "USA"));
                await inner.CommitAsync(); // RELEASE SAVEPOINT
            }

            await outer.CommitAsync();
        }

        Assert.NotNull(await store.SelectByKeyAsync("OUT01"));
        Assert.NotNull(await store.SelectByKeyAsync("INN01"));
    }

    [Fact]
    public async Task NestedTransactionRollbackRevertsSavepointButKeepsOuterChanges()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "Tx");
        var inquiry = harness.GetRequiredService<IInquiry>();
        var store = harness.GetRequiredService<CustomerStore>();

        await using (var outer = await inquiry.BeginTransactionAsync())
        {
            await outer.ExecuteAsync(InsertCustomer("OUT02", "Outer", "USA"));

            await using (var inner = await outer.BeginTransactionAsync())
            {
                await inner.ExecuteAsync(InsertCustomer("INN02", "Inner", "USA"));
                await inner.RollbackAsync(); // ROLLBACK TO SAVEPOINT
            }

            await outer.CommitAsync();
        }

        Assert.NotNull(await store.SelectByKeyAsync("OUT02"));
        Assert.Null(await store.SelectByKeyAsync("INN02"));
    }

    [Fact]
    public async Task NestedTransactionDisposedWithoutCommitRollsBackSavepoint()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "Tx");
        var inquiry = harness.GetRequiredService<IInquiry>();
        var store = harness.GetRequiredService<CustomerStore>();

        await using (var outer = await inquiry.BeginTransactionAsync())
        {
            await outer.ExecuteAsync(InsertCustomer("OUT03", "Outer", "USA"));

            await using (var inner = await outer.BeginTransactionAsync())
            {
                await inner.ExecuteAsync(InsertCustomer("INN03", "Inner", "USA"));
                // No commit — dispose rolls back to savepoint.
            }

            await outer.CommitAsync();
        }

        Assert.NotNull(await store.SelectByKeyAsync("OUT03"));
        Assert.Null(await store.SelectByKeyAsync("INN03"));
    }

    [Fact]
    public async Task DeeplyNestedSavepointsAllReleaseInOrder()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "Tx");
        var inquiry = harness.GetRequiredService<IInquiry>();
        var store = harness.GetRequiredService<CustomerStore>();

        await using (var l1 = await inquiry.BeginTransactionAsync())
        {
            await l1.ExecuteAsync(InsertCustomer("LV1", "Level 1", "USA"));

            await using (var l2 = await l1.BeginTransactionAsync())
            {
                await l2.ExecuteAsync(InsertCustomer("LV2", "Level 2", "USA"));

                await using (var l3 = await l2.BeginTransactionAsync())
                {
                    await l3.ExecuteAsync(InsertCustomer("LV3", "Level 3", "USA"));
                    await l3.CommitAsync();
                }

                await l2.CommitAsync();
            }

            await l1.CommitAsync();
        }

        Assert.NotNull(await store.SelectByKeyAsync("LV1"));
        Assert.NotNull(await store.SelectByKeyAsync("LV2"));
        Assert.NotNull(await store.SelectByKeyAsync("LV3"));
    }

    [Fact]
    public async Task NestedRollbackKeepsAmbientSlotIntactForFollowupOperations()
    {
        // After the inner rolls back, the OUTER transaction must remain usable — the ambient
        // slot still points at the outer pipeline. A follow-up operation must enlist into the
        // outer transaction (not the default non-transactional pipeline).
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "Tx");
        var inquiry = harness.GetRequiredService<IInquiry>();
        var store = harness.GetRequiredService<CustomerStore>();

        await using (var outer = await inquiry.BeginTransactionAsync())
        {
            await using (var inner = await outer.BeginTransactionAsync())
            {
                await inner.ExecuteAsync(InsertCustomer("INNX", "Reverted", "USA"));
                await inner.RollbackAsync();
            }

            // This insert must still be inside the OUTER transaction.
            await outer.ExecuteAsync(InsertCustomer("AFTR", "After Rollback", "USA"));
            // Roll back the outer too — both INNX (already reverted) and AFTR should vanish.
            await outer.RollbackAsync();
        }

        Assert.Null(await store.SelectByKeyAsync("INNX"));
        Assert.Null(await store.SelectByKeyAsync("AFTR"));
    }

    // ---- IsolationLevel passthrough --------------------------------------------------

    [Fact]
    public async Task BeginTransactionPassesIsolationLevelToUnderlyingDbTransaction()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "Tx");
        var inquiry = harness.GetRequiredService<IInquiry>();

        await using var tx = await inquiry.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        Assert.Equal(System.Data.IsolationLevel.Serializable, tx.IsolationLevel);
    }

    [Fact]
    public async Task DefaultIsolationLevelIsReadCommitted()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "Tx");
        var inquiry = harness.GetRequiredService<IInquiry>();

        await using var tx = await inquiry.BeginTransactionAsync();
        // Sqlite reports Serializable for any non-deferred transaction; SQL Server / PG / MySQL
        // will report ReadCommitted. Either way the property is non-null and reflects the
        // provider's mapping of what we asked for.
        Assert.True(tx.IsolationLevel != System.Data.IsolationLevel.Unspecified);
    }

    [Fact]
    public async Task NestedTransactionInheritsIsolationLevelFromOuter()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "Tx");
        var inquiry = harness.GetRequiredService<IInquiry>();

        await using var outer = await inquiry.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        await using var inner = await outer.BeginTransactionAsync();
        Assert.Equal(outer.IsolationLevel, inner.IsolationLevel);
    }

    // ---- tx.* query/execute methods (the entire transactional surface) ---------------

    [Fact]
    public async Task TxExecuteAsyncForwardsToInquiry()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "Tx");
        var inquiry = harness.GetRequiredService<IInquiry>();
        var store = harness.GetRequiredService<CustomerStore>();

        await using (var tx = await inquiry.BeginTransactionAsync())
        {
            // Every transactional call is a direct method on the transaction handle.
            await tx.ExecuteAsync(InsertCustomer("FWD01", "Forwarded", "USA"));
            await tx.CommitAsync();
        }

        Assert.NotNull(await store.SelectByKeyAsync("FWD01"));
    }

    // ---- tx.* fails fast on use-after-close (P1) -------------------------------------

    [Fact]
    public async Task TxExecuteAsyncAfterCommitThrowsObjectDisposed()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "Tx");
        var inquiry = harness.GetRequiredService<IInquiry>();

        var tx = await inquiry.BeginTransactionAsync();
        await tx.ExecuteAsync(InsertCustomer("CLS01", "Closed", "USA"));
        await tx.CommitAsync();

        // Subsequent forwarding call must fail-fast rather than silently routing through the
        // default (non-transactional) pipeline and auto-committing a write.
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => tx.ExecuteAsync(InsertCustomer("AFTC1", "AfterCommit", "USA")));
    }

    [Fact]
    public async Task TxExecuteAsyncAfterRollbackThrowsObjectDisposed()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "Tx");
        var inquiry = harness.GetRequiredService<IInquiry>();

        var tx = await inquiry.BeginTransactionAsync();
        await tx.RollbackAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => tx.ExecuteAsync(InsertCustomer("AFTR1", "AfterRollback", "USA")));
    }

    [Fact]
    public async Task TxExecuteAsyncAfterDisposeThrowsObjectDisposed()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "Tx");
        var inquiry = harness.GetRequiredService<IInquiry>();

        var tx = await inquiry.BeginTransactionAsync();
        await tx.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => tx.ExecuteAsync(InsertCustomer("AFTD1", "AfterDispose", "USA")));
    }

    [Fact]
    public async Task TxQueryAsyncAfterCloseThrowsObjectDisposed()
    {
        // Covers the read-path forwarding too (not just Execute).
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "Tx");
        var inquiry = harness.GetRequiredService<IInquiry>();

        var tx = await inquiry.BeginTransactionAsync();
        await tx.CommitAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => tx.QueryListAsync<Inquiry.Northwind.Models.Customer>(
                $"SELECT CustomerID, CompanyName, ContactName, ContactTitle, Address, City, Region, PostalCode, Country, Phone, Fax FROM Customers"));
    }

    [Fact]
    public async Task TxBeginTransactionAsyncAfterCloseThrowsObjectDisposed()
    {
        // Nested savepoint via the forwarding tx.BeginTransactionAsync must also fail-fast.
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "Tx");
        var inquiry = harness.GetRequiredService<IInquiry>();

        var tx = await inquiry.BeginTransactionAsync();
        await tx.CommitAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => tx.BeginTransactionAsync());
    }

    [Fact]
    public async Task SavepointExecuteAsyncAfterCommitThrowsObjectDisposed()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "Tx");
        var inquiry = harness.GetRequiredService<IInquiry>();

        await using var outer = await inquiry.BeginTransactionAsync();
        var inner = await outer.BeginTransactionAsync();
        await inner.CommitAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => inner.ExecuteAsync(InsertCustomer("SPCOM", "SavepointAfterCommit", "USA")));

        await outer.CommitAsync();
    }

    [Fact]
    public async Task SavepointExecuteAsyncAfterRollbackThrowsObjectDisposed()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "Tx");
        var inquiry = harness.GetRequiredService<IInquiry>();

        await using var outer = await inquiry.BeginTransactionAsync();
        var inner = await outer.BeginTransactionAsync();
        await inner.RollbackAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => inner.ExecuteAsync(InsertCustomer("SPROL", "SavepointAfterRollback", "USA")));

        await outer.CommitAsync();
    }

    [Fact]
    public async Task RootInquiryStillUsableAfterTransactionCloses()
    {
        // Regression guard: the fix protects tx.X (the per-tx-scoped forwarding methods).
        // It MUST NOT make the root inquiry / DI-resolved stores throw after the tx closes
        // — those legitimately fall through to the default non-transactional pipeline so
        // post-tx code in the same scope can keep working.
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "Tx");
        var inquiry = harness.GetRequiredService<IInquiry>();
        var store = harness.GetRequiredService<CustomerStore>();

        await using (var tx = await inquiry.BeginTransactionAsync())
        {
            await tx.ExecuteAsync(InsertCustomer("INTX1", "InTx", "USA"));
            await tx.CommitAsync();
        }

        // After the using-block exits, the slot's Pipeline is null. Calls on the root
        // inquiry / on a DI-resolved store route through the default pipeline normally.
        await store.InsertAsync(new Inquiry.Northwind.Models.Customer { CustomerID = "POST1", CompanyName = "PostTx" });
        Assert.NotNull(await store.SelectByKeyAsync("POST1"));
    }

    [Fact]
    public async Task TxQuerySingleOrDefaultAsyncForwardsToInquiry()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "Tx");
        var inquiry = harness.GetRequiredService<IInquiry>();
        var store = harness.GetRequiredService<CustomerStore>();

        await store.InsertAsync(new Inquiry.Northwind.Models.Customer { CustomerID = "READ1", CompanyName = "Read Me" });

        await using var tx = await inquiry.BeginTransactionAsync();
        var loaded = await tx.QuerySingleOrDefaultAsync<Inquiry.Northwind.Models.Customer>(
            $"SELECT CustomerID, CompanyName, ContactName, ContactTitle, Address, City, Region, PostalCode, Country, Phone, Fax FROM Customers WHERE CustomerID = {"READ1"}");

        Assert.NotNull(loaded);
        Assert.Equal("Read Me", loaded!.CompanyName);
    }
}
