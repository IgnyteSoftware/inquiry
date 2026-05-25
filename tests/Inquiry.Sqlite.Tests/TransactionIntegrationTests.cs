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
        await tx.Inquiry.ExecuteAsync(InsertCustomerSql, new { CustomerID = "TX001", CompanyName = "Widget", Country = "USA" });
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
            await tx.Inquiry.ExecuteAsync(InsertCustomerSql, new { CustomerID = "GHOST", CompanyName = "Ghost", Country = "USA" });
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
        await tx.Inquiry.ExecuteAsync(InsertCustomerSql, new { CustomerID = "REV01", CompanyName = "Reverted", Country = "USA" });
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
            await tx.Inquiry.ExecuteAsync(InsertCustomerSql, new { CustomerID = $"M{i:D4}", CompanyName = $"Customer {i}", Country = "USA" });
        }
        await tx.CommitAsync();

        var all = await store.SelectAllAsync().ToListAsync();
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

    [Fact]
    public async Task NestedTransactionThrowsInvalidOperation()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "Tx");
        var inquiry = harness.GetRequiredService<IInquiry>();

        await using var outer = await inquiry.BeginTransactionAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => outer.Inquiry.BeginTransactionAsync());
    }
}
