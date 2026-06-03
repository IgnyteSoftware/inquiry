using Inquiry.Northwind;
using Inquiry.Northwind.Stores;
using Inquiry.Sqlite.Tests.Fixtures;

namespace Inquiry.Sqlite.Tests;

public sealed class TransactionIntegrationTests
{
    private const string InsertCustomerSql =
        "INSERT INTO Customers (CustomerID, CompanyName, Country) VALUES (@CustomerID, @CompanyName, @Country)";

    [Fact]
    public async Task CommittedTransactionPersistsChanges()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "Tx");
        var inquiry = harness.GetRequiredService<IInquiry>();
        var store = harness.GetRequiredService<CustomerStore>();

        await using var tx = await inquiry.BeginTransactionAsync();
        await tx.ExecuteAsync(InsertCustomerSql, new { CustomerID = "TX001", CompanyName = "Widget", Country = "USA" });
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
            await tx.ExecuteAsync(InsertCustomerSql, new { CustomerID = "GHOST", CompanyName = "Ghost", Country = "USA" });
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
        await tx.ExecuteAsync(InsertCustomerSql, new { CustomerID = "REV01", CompanyName = "Reverted", Country = "USA" });
        await tx.RollbackAsync();

        var loaded = await store.SelectByKeyAsync("REV01");
        Assert.Null(loaded);
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
            await tx.ExecuteAsync(InsertCustomerSql, new { CustomerID = $"M{i:D4}", CompanyName = $"Customer {i}", Country = "USA" });
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
    // Gated on NET8_0_OR_GREATER: Microsoft.Data.Sqlite's netstandard2.0 build (used on net6 / net7)
    // can't override DbTransaction.Save because the API didn't exist in netstandard2.0, so
    // SavepointInquiryTransaction throws NotSupportedException at runtime on those TFMs.
    // DefaultInquiry.BeginSavepointAsync gates the entry point and throws
    // PlatformNotSupportedException with a clear message on net6 / net7.

#if NET8_0_OR_GREATER
    [Fact]
    public async Task NestedTransactionCommitReleasesSavepointAndOuterCommitPersistsBoth()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "Tx");
        var inquiry = harness.GetRequiredService<IInquiry>();
        var store = harness.GetRequiredService<CustomerStore>();

        await using (var outer = await inquiry.BeginTransactionAsync())
        {
            await outer.ExecuteAsync(InsertCustomerSql, new { CustomerID = "OUT01", CompanyName = "Outer", Country = "USA" });

            await using (var inner = await outer.BeginTransactionAsync())
            {
                await inner.ExecuteAsync(InsertCustomerSql, new { CustomerID = "INN01", CompanyName = "Inner", Country = "USA" });
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
            await outer.ExecuteAsync(InsertCustomerSql, new { CustomerID = "OUT02", CompanyName = "Outer", Country = "USA" });

            await using (var inner = await outer.BeginTransactionAsync())
            {
                await inner.ExecuteAsync(InsertCustomerSql, new { CustomerID = "INN02", CompanyName = "Inner", Country = "USA" });
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
            await outer.ExecuteAsync(InsertCustomerSql, new { CustomerID = "OUT03", CompanyName = "Outer", Country = "USA" });

            await using (var inner = await outer.BeginTransactionAsync())
            {
                await inner.ExecuteAsync(InsertCustomerSql, new { CustomerID = "INN03", CompanyName = "Inner", Country = "USA" });
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
            await l1.ExecuteAsync(InsertCustomerSql, new { CustomerID = "LV1", CompanyName = "Level 1", Country = "USA" });

            await using (var l2 = await l1.BeginTransactionAsync())
            {
                await l2.ExecuteAsync(InsertCustomerSql, new { CustomerID = "LV2", CompanyName = "Level 2", Country = "USA" });

                await using (var l3 = await l2.BeginTransactionAsync())
                {
                    await l3.ExecuteAsync(InsertCustomerSql, new { CustomerID = "LV3", CompanyName = "Level 3", Country = "USA" });
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
                await inner.ExecuteAsync(InsertCustomerSql, new { CustomerID = "INNX", CompanyName = "Reverted", Country = "USA" });
                await inner.RollbackAsync();
            }

            // This insert must still be inside the OUTER transaction.
            await outer.ExecuteAsync(InsertCustomerSql, new { CustomerID = "AFTR", CompanyName = "After Rollback", Country = "USA" });
            // Roll back the outer too — both INNX (already reverted) and AFTR should vanish.
            await outer.RollbackAsync();
        }

        Assert.Null(await store.SelectByKeyAsync("INNX"));
        Assert.Null(await store.SelectByKeyAsync("AFTR"));
    }
#endif

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

#if NET8_0_OR_GREATER
    // Savepoint-dependent (uses tx.BeginTransactionAsync). See top-of-file gate rationale.
    [Fact]
    public async Task NestedTransactionInheritsIsolationLevelFromOuter()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "Tx");
        var inquiry = harness.GetRequiredService<IInquiry>();

        await using var outer = await inquiry.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        await using var inner = await outer.BeginTransactionAsync();
        Assert.Equal(outer.IsolationLevel, inner.IsolationLevel);
    }
#endif

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
            await tx.ExecuteAsync(InsertCustomerSql, new { CustomerID = "FWD01", CompanyName = "Forwarded", Country = "USA" });
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
        await tx.ExecuteAsync(InsertCustomerSql, new { CustomerID = "CLS01", CompanyName = "Closed", Country = "USA" });
        await tx.CommitAsync();

        // Subsequent forwarding call must fail-fast rather than silently routing through the
        // default (non-transactional) pipeline and auto-committing a write.
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => tx.ExecuteAsync(InsertCustomerSql, new { CustomerID = "AFTC1", CompanyName = "AfterCommit", Country = "USA" }));
    }

    [Fact]
    public async Task TxExecuteAsyncAfterRollbackThrowsObjectDisposed()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "Tx");
        var inquiry = harness.GetRequiredService<IInquiry>();

        var tx = await inquiry.BeginTransactionAsync();
        await tx.RollbackAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => tx.ExecuteAsync(InsertCustomerSql, new { CustomerID = "AFTR1", CompanyName = "AfterRollback", Country = "USA" }));
    }

    [Fact]
    public async Task TxExecuteAsyncAfterDisposeThrowsObjectDisposed()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "Tx");
        var inquiry = harness.GetRequiredService<IInquiry>();

        var tx = await inquiry.BeginTransactionAsync();
        await tx.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => tx.ExecuteAsync(InsertCustomerSql, new { CustomerID = "AFTD1", CompanyName = "AfterDispose", Country = "USA" }));
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
                "SELECT CustomerID, CompanyName, ContactName, ContactTitle, Address, City, Region, PostalCode, Country, Phone, Fax FROM Customers"));
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

#if NET8_0_OR_GREATER
    // Savepoint-dependent (uses tx.BeginTransactionAsync). See top-of-file gate rationale.
    [Fact]
    public async Task SavepointExecuteAsyncAfterCommitThrowsObjectDisposed()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "Tx");
        var inquiry = harness.GetRequiredService<IInquiry>();

        await using var outer = await inquiry.BeginTransactionAsync();
        var inner = await outer.BeginTransactionAsync();
        await inner.CommitAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => inner.ExecuteAsync(InsertCustomerSql, new { CustomerID = "SPCOM", CompanyName = "SavepointAfterCommit", Country = "USA" }));

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
            () => inner.ExecuteAsync(InsertCustomerSql, new { CustomerID = "SPROL", CompanyName = "SavepointAfterRollback", Country = "USA" }));

        await outer.CommitAsync();
    }
#endif

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
            await tx.ExecuteAsync(InsertCustomerSql, new { CustomerID = "INTX1", CompanyName = "InTx", Country = "USA" });
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
            "SELECT CustomerID, CompanyName, ContactName, ContactTitle, Address, City, Region, PostalCode, Country, Phone, Fax FROM Customers WHERE CustomerID = @id",
            new { id = "READ1" });

        Assert.NotNull(loaded);
        Assert.Equal("Read Me", loaded!.CompanyName);
    }
}
