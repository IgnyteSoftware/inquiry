using Inquiry.Northwind.Models;
using Inquiry.Northwind.Stores;
using Inquiry.Oracle.Tests.Fixtures;

namespace Inquiry.Oracle.Tests;

/// <summary>
/// Tests for the ambient-transaction mechanism on <see cref="DefaultInquiry"/>: when a
/// transaction is begun, all subsequent IInquiry calls in the same async control flow
/// (including those routed through generated stores) participate in that transaction
/// automatically.
/// </summary>
[Collection(OracleCollection.Name)]
public sealed class AmbientTransactionIntegrationTests
{
    private readonly OracleContainerFixture _fixture;
    public AmbientTransactionIntegrationTests(OracleContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task StoreCallInsideTransactionIsRolledBack()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "Ambient");
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

    [SkippableFact]
    public async Task StoreCallInsideTransactionIsCommitted()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "Ambient");
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

    [SkippableFact]
    public async Task MultipleStoresInsideOneTransactionShareTheSameConnection()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "Ambient");
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

    [SkippableFact]
    public async Task StoreCallOutsideTransactionUsesDefaultPipeline()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "Ambient");
        var inquiry = harness.GetRequiredService<IInquiry>();
        var store = harness.GetRequiredService<CustomerStore>();

        // No transaction — should hit the default pipeline and persist immediately.
        await store.InsertAsync(new Customer { CustomerID = "DEF01", CompanyName = "Default" });

        var loaded = await store.SelectByKeyAsync("DEF01");
        Assert.NotNull(loaded);
    }

    [SkippableFact]
    public async Task AmbientSlotIsRestoredAfterTransactionDisposes()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "Ambient");
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

    [SkippableFact]
    public async Task StoreCallCapturedInsideTransactionThrowsAfterTransactionCloses()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "Ambient");
        var inquiry = harness.GetRequiredService<IInquiry>();
        var store = harness.GetRequiredService<CustomerStore>();
        var releaseStraggler = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Task straggler;
        await using (var tx = await inquiry.BeginTransactionAsync())
        {
            straggler = Task.Run(async () =>
            {
                await releaseStraggler.Task;
                await store.InsertAsync(new Customer { CustomerID = "LATE1", CompanyName = "Late" });
            });

            await tx.CommitAsync();
        }

        releaseStraggler.SetResult();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => straggler);
        Assert.Null(await store.SelectByKeyAsync("LATE1"));

        await store.InsertAsync(new Customer { CustomerID = "POST1", CompanyName = "Post Transaction" });
        Assert.NotNull(await store.SelectByKeyAsync("POST1"));
    }

    [SkippableFact]
    public async Task BackToBackTransactionsEachOpenFreshPipeline()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "Ambient");
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

    [SkippableFact]
    public async Task ParallelTransactionsOnSeparateAsyncFlowsDoNotInterfere()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harnessA = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "AmbientA");
        await using var harnessB = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "AmbientB");

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

    [SkippableFact]
    public async Task ConcurrentOperationsInsideOneTransactionThrow()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "Ambient");
        var inquiry = harness.GetRequiredService<IInquiry>();
        var store = harness.GetRequiredService<CustomerStore>();

        // Seed two rows OUTSIDE the transaction so the in-tx SelectAll has rows to stream.
        await store.InsertAsync(new Customer { CustomerID = "SEED1", CompanyName = "Seed 1" });
        await store.InsertAsync(new Customer { CustomerID = "SEED2", CompanyName = "Seed 2" });

        await using var tx = await inquiry.BeginTransactionAsync();

        var streaming = tx.QueryAsync<Customer>(
            $"SELECT CustomerID, CompanyName, ContactName, ContactTitle, Address, City, Region, PostalCode, Country, Phone, Fax FROM Customers");
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
}
