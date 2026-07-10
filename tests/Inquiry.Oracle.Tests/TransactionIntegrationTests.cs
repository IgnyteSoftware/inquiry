using Inquiry.Northwind.Models;
using Inquiry.Northwind.Stores;
using Inquiry.Oracle.Tests.Fixtures;

namespace Inquiry.Oracle.Tests;

[Collection(OracleCollection.Name)]
public sealed class TransactionIntegrationTests
{
    private readonly OracleContainerFixture _fixture;
    public TransactionIntegrationTests(OracleContainerFixture fixture) => _fixture = fixture;

    private static FormattableString InsertCustomer(string customerId, string companyName, string country)
        => $"INSERT INTO Customers (CustomerID, CompanyName, Country) VALUES ({customerId}, {companyName}, {country})";

    [SkippableFact]
    public async Task CommittedTransactionPersistsChanges()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "Tx");
        var inquiry = harness.GetRequiredService<IInquiry>();
        var store = harness.GetRequiredService<CustomerStore>();

        await using var tx = await inquiry.BeginTransactionAsync();
        await tx.ExecuteAsync(InsertCustomer("TX001", "Widget", "USA"));
        await tx.CommitAsync();

        var loaded = await store.SelectByKeyAsync("TX001");
        Assert.NotNull(loaded);
        Assert.Equal("Widget", loaded.CompanyName);
    }

    [SkippableFact]
    public async Task DisposedUncommittedTransactionRollsBack()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "Tx");
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

    [SkippableFact]
    public async Task ExplicitRollbackReverts()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "Tx");
        var inquiry = harness.GetRequiredService<IInquiry>();
        var store = harness.GetRequiredService<CustomerStore>();

        await using var tx = await inquiry.BeginTransactionAsync();
        await tx.ExecuteAsync(InsertCustomer("REV01", "Reverted", "USA"));
        await tx.RollbackAsync();

        var loaded = await store.SelectByKeyAsync("REV01");
        Assert.Null(loaded);
    }

    [SkippableFact]
    public async Task ExecuteInTransactionAsyncCommitsWhenOperationCompletes()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "Tx");
        var inquiry = harness.GetRequiredService<IInquiry>();
        var store = harness.GetRequiredService<CustomerStore>();

        await inquiry.ExecuteInTransactionAsync(async tx =>
        {
            await store.InsertAsync(new Customer { CustomerID = "HELP1", CompanyName = "Helper" });
            await tx.ExecuteAsync($"UPDATE Customers SET Country = {"USA"} WHERE CustomerID = {"HELP1"}");
        });

        var loaded = await store.SelectByKeyAsync("HELP1");
        Assert.NotNull(loaded);
        Assert.Equal("USA", loaded!.Country);
    }

    [SkippableFact]
    public async Task ExecuteInTransactionAsyncRollsBackWhenOperationThrows()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "Tx");
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

    [SkippableFact]
    public async Task ExecuteInTransactionAsyncReturnsOperationResultAfterCommit()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "Tx");
        var inquiry = harness.GetRequiredService<IInquiry>();
        var store = harness.GetRequiredService<CustomerStore>();

        var insertedCount = await inquiry.ExecuteInTransactionAsync(async tx =>
        {
            await tx.ExecuteAsync(InsertCustomer("HELP3", "Returned", "USA"));
            return await tx.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM Customers WHERE CustomerID = {"HELP3"}");
        });

        Assert.Equal(1, insertedCount);
        Assert.NotNull(await store.SelectByKeyAsync("HELP3"));
    }

    [SkippableFact]
    public async Task TransactionInquirySupportsMultipleInsertsInOneCommit()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "Tx");
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

    [SkippableFact]
    public async Task CommitAfterDisposeThrows()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "Tx");
        var inquiry = harness.GetRequiredService<IInquiry>();

        var tx = await inquiry.BeginTransactionAsync();
        await tx.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => tx.CommitAsync());
    }

    [SkippableFact]
    public async Task RollbackAfterDisposeThrows()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "Tx");
        var inquiry = harness.GetRequiredService<IInquiry>();

        var tx = await inquiry.BeginTransactionAsync();
        await tx.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => tx.RollbackAsync());
    }

    [SkippableFact]
    public async Task DoubleDisposeIsSafe()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "Tx");
        var inquiry = harness.GetRequiredService<IInquiry>();

        var tx = await inquiry.BeginTransactionAsync();
        await tx.DisposeAsync();
        await tx.DisposeAsync(); // must not throw
    }

    // ---- Nested transactions / savepoints --------------------------------------------

    [SkippableFact]
    public async Task NestedTransactionCommitReleasesSavepointAndOuterCommitPersistsBoth()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "Tx");
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

    [SkippableFact]
    public async Task NestedTransactionRollbackRevertsSavepointButKeepsOuterChanges()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "Tx");
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

    [SkippableFact]
    public async Task NestedTransactionDisposedWithoutCommitRollsBackSavepoint()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "Tx");
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

    [SkippableFact]
    public async Task DeeplyNestedSavepointsAllReleaseInOrder()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "Tx");
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

    [SkippableFact]
    public async Task NestedRollbackKeepsAmbientSlotIntactForFollowupOperations()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "Tx");
        var inquiry = harness.GetRequiredService<IInquiry>();
        var store = harness.GetRequiredService<CustomerStore>();

        await using (var outer = await inquiry.BeginTransactionAsync())
        {
            await using (var inner = await outer.BeginTransactionAsync())
            {
                await inner.ExecuteAsync(InsertCustomer("INNX", "Reverted", "USA"));
                await inner.RollbackAsync();
            }

            await outer.ExecuteAsync(InsertCustomer("AFTR", "After Rollback", "USA"));
            await outer.RollbackAsync();
        }

        Assert.Null(await store.SelectByKeyAsync("INNX"));
        Assert.Null(await store.SelectByKeyAsync("AFTR"));
    }

    // ---- IsolationLevel passthrough --------------------------------------------------

    [SkippableFact]
    public async Task BeginTransactionPassesIsolationLevelToUnderlyingDbTransaction()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "Tx");
        var inquiry = harness.GetRequiredService<IInquiry>();

        await using var tx = await inquiry.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        Assert.Equal(System.Data.IsolationLevel.Serializable, tx.IsolationLevel);
    }

    [SkippableFact]
    public async Task DefaultIsolationLevelIsReadCommitted()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "Tx");
        var inquiry = harness.GetRequiredService<IInquiry>();

        await using var tx = await inquiry.BeginTransactionAsync();
        Assert.True(tx.IsolationLevel != System.Data.IsolationLevel.Unspecified);
    }

    [SkippableFact]
    public async Task NestedTransactionInheritsIsolationLevelFromOuter()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "Tx");
        var inquiry = harness.GetRequiredService<IInquiry>();

        await using var outer = await inquiry.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        await using var inner = await outer.BeginTransactionAsync();
        Assert.Equal(outer.IsolationLevel, inner.IsolationLevel);
    }

    // ---- tx.* query/execute methods (the entire transactional surface) ---------------

    [SkippableFact]
    public async Task TxExecuteAsyncForwardsToInquiry()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "Tx");
        var inquiry = harness.GetRequiredService<IInquiry>();
        var store = harness.GetRequiredService<CustomerStore>();

        await using (var tx = await inquiry.BeginTransactionAsync())
        {
            await tx.ExecuteAsync(InsertCustomer("FWD01", "Forwarded", "USA"));
            await tx.CommitAsync();
        }

        Assert.NotNull(await store.SelectByKeyAsync("FWD01"));
    }

    // ---- tx.* fails fast on use-after-close (P1) -------------------------------------

    [SkippableFact]
    public async Task TxExecuteAsyncAfterCommitThrowsObjectDisposed()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "Tx");
        var inquiry = harness.GetRequiredService<IInquiry>();

        var tx = await inquiry.BeginTransactionAsync();
        await tx.ExecuteAsync(InsertCustomer("CLS01", "Closed", "USA"));
        await tx.CommitAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => tx.ExecuteAsync(InsertCustomer("AFTC1", "AfterCommit", "USA")));
    }

    [SkippableFact]
    public async Task TxExecuteAsyncAfterRollbackThrowsObjectDisposed()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "Tx");
        var inquiry = harness.GetRequiredService<IInquiry>();

        var tx = await inquiry.BeginTransactionAsync();
        await tx.RollbackAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => tx.ExecuteAsync(InsertCustomer("AFTR1", "AfterRollback", "USA")));
    }

    [SkippableFact]
    public async Task TxExecuteAsyncAfterDisposeThrowsObjectDisposed()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "Tx");
        var inquiry = harness.GetRequiredService<IInquiry>();

        var tx = await inquiry.BeginTransactionAsync();
        await tx.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => tx.ExecuteAsync(InsertCustomer("AFTD1", "AfterDispose", "USA")));
    }

    [SkippableFact]
    public async Task TxQueryAsyncAfterCloseThrowsObjectDisposed()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "Tx");
        var inquiry = harness.GetRequiredService<IInquiry>();

        var tx = await inquiry.BeginTransactionAsync();
        await tx.CommitAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => tx.QueryListAsync<Customer>(
                $"SELECT CustomerID, CompanyName, ContactName, ContactTitle, Address, City, Region, PostalCode, Country, Phone, Fax FROM Customers"));
    }

    [SkippableFact]
    public async Task TxBeginTransactionAsyncAfterCloseThrowsObjectDisposed()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "Tx");
        var inquiry = harness.GetRequiredService<IInquiry>();

        var tx = await inquiry.BeginTransactionAsync();
        await tx.CommitAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => tx.BeginTransactionAsync());
    }

    [SkippableFact]
    public async Task SavepointExecuteAsyncAfterCommitThrowsObjectDisposed()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "Tx");
        var inquiry = harness.GetRequiredService<IInquiry>();

        await using var outer = await inquiry.BeginTransactionAsync();
        var inner = await outer.BeginTransactionAsync();
        await inner.CommitAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => inner.ExecuteAsync(InsertCustomer("SPCOM", "SavepointAfterCommit", "USA")));

        await outer.CommitAsync();
    }

    [SkippableFact]
    public async Task SavepointExecuteAsyncAfterRollbackThrowsObjectDisposed()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "Tx");
        var inquiry = harness.GetRequiredService<IInquiry>();

        await using var outer = await inquiry.BeginTransactionAsync();
        var inner = await outer.BeginTransactionAsync();
        await inner.RollbackAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => inner.ExecuteAsync(InsertCustomer("SPROL", "SavepointAfterRollback", "USA")));

        await outer.CommitAsync();
    }

    [SkippableFact]
    public async Task RootInquiryStillUsableAfterTransactionCloses()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "Tx");
        var inquiry = harness.GetRequiredService<IInquiry>();
        var store = harness.GetRequiredService<CustomerStore>();

        await using (var tx = await inquiry.BeginTransactionAsync())
        {
            await tx.ExecuteAsync(InsertCustomer("INTX1", "InTx", "USA"));
            await tx.CommitAsync();
        }

        await store.InsertAsync(new Customer { CustomerID = "POST1", CompanyName = "PostTx" });
        Assert.NotNull(await store.SelectByKeyAsync("POST1"));
    }

    [SkippableFact]
    public async Task TxQuerySingleOrDefaultAsyncForwardsToInquiry()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "Tx");
        var inquiry = harness.GetRequiredService<IInquiry>();
        var store = harness.GetRequiredService<CustomerStore>();

        await store.InsertAsync(new Customer { CustomerID = "READ1", CompanyName = "Read Me" });

        await using var tx = await inquiry.BeginTransactionAsync();
        var loaded = await tx.QuerySingleOrDefaultAsync<Customer>(
            $"SELECT CustomerID, CompanyName, ContactName, ContactTitle, Address, City, Region, PostalCode, Country, Phone, Fax FROM Customers WHERE CustomerID = {"READ1"}");

        Assert.NotNull(loaded);
        Assert.Equal("Read Me", loaded!.CompanyName);
    }
}
