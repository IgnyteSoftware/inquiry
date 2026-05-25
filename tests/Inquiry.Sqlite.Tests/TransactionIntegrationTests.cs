using Inquiry.Sqlite.Tests.Fixtures;

namespace Inquiry.Sqlite.Tests;

public sealed class TransactionIntegrationTests
{
    [Fact]
    public async Task CommittedTransactionPersistsChanges()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Schemas.Product, "Tx");
        var inquiry = harness.GetRequiredService<IInquiry>();
        var store = harness.GetRequiredService<ProductStore>();

        await using var tx = await inquiry.BeginTransactionAsync();
        var product = new Product { Key = Guid.NewGuid(), Name = "Widget", Price = 9.99m, CategoryKey = Guid.Empty };
        await tx.Inquiry.ExecuteAsync(
            "INSERT INTO TProduct (Key, Name, Price, CategoryKey) VALUES (@Key, @Name, @Price, @CategoryKey)",
            new { product.Key, product.Name, product.Price, product.CategoryKey });
        await tx.CommitAsync();

        var loaded = await store.SelectByKeyAsync(product.Key);
        Assert.NotNull(loaded);
        Assert.Equal("Widget", loaded.Name);
    }

    [Fact]
    public async Task DisposedUncommittedTransactionRollsBack()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Schemas.Product, "Tx");
        var inquiry = harness.GetRequiredService<IInquiry>();
        var store = harness.GetRequiredService<ProductStore>();

        var key = Guid.NewGuid();
        await using (var tx = await inquiry.BeginTransactionAsync())
        {
            await tx.Inquiry.ExecuteAsync(
                "INSERT INTO TProduct (Key, Name, Price, CategoryKey) VALUES (@Key, @Name, @Price, @CategoryKey)",
                new { Key = key, Name = "Ghost", Price = 1m, CategoryKey = Guid.Empty });
            // No commit — dispose rolls back
        }

        var loaded = await store.SelectByKeyAsync(key);
        Assert.Null(loaded);
    }

    [Fact]
    public async Task ExplicitRollbackReverts()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Schemas.Product, "Tx");
        var inquiry = harness.GetRequiredService<IInquiry>();
        var store = harness.GetRequiredService<ProductStore>();

        var key = Guid.NewGuid();
        await using var tx = await inquiry.BeginTransactionAsync();
        await tx.Inquiry.ExecuteAsync(
            "INSERT INTO TProduct (Key, Name, Price, CategoryKey) VALUES (@Key, @Name, @Price, @CategoryKey)",
            new { Key = key, Name = "Reverted", Price = 5m, CategoryKey = Guid.Empty });
        await tx.RollbackAsync();

        var loaded = await store.SelectByKeyAsync(key);
        Assert.Null(loaded);
    }

    [Fact]
    public async Task TransactionInquirySupportsMultipleInsertsInOneCommit()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Schemas.Product, "Tx");
        var inquiry = harness.GetRequiredService<IInquiry>();
        var store = harness.GetRequiredService<ProductStore>();

        var products = Enumerable.Range(1, 5).Select(i => new Product
        {
            Key = Guid.NewGuid(),
            Name = $"Product {i}",
            Price = i * 10m,
            CategoryKey = Guid.Empty
        }).ToList();

        await using var tx = await inquiry.BeginTransactionAsync();
        foreach (var p in products)
        {
            await tx.Inquiry.ExecuteAsync(
                "INSERT INTO TProduct (Key, Name, Price, CategoryKey) VALUES (@Key, @Name, @Price, @CategoryKey)",
                new { p.Key, p.Name, p.Price, p.CategoryKey });
        }
        await tx.CommitAsync();

        var all = await store.SelectAllAsync().ToListAsync();
        Assert.Equal(5, all.Count);
    }

    [Fact]
    public async Task CommitAfterDisposeThrows()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Schemas.Product, "Tx");
        var inquiry = harness.GetRequiredService<IInquiry>();

        var tx = await inquiry.BeginTransactionAsync();
        await tx.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => tx.CommitAsync());
    }

    [Fact]
    public async Task RollbackAfterDisposeThrows()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Schemas.Product, "Tx");
        var inquiry = harness.GetRequiredService<IInquiry>();

        var tx = await inquiry.BeginTransactionAsync();
        await tx.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => tx.RollbackAsync());
    }

    [Fact]
    public async Task DoubleDisposeIsSafe()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Schemas.Product, "Tx");
        var inquiry = harness.GetRequiredService<IInquiry>();

        var tx = await inquiry.BeginTransactionAsync();
        await tx.DisposeAsync();
        await tx.DisposeAsync(); // must not throw
    }

    [Fact]
    public async Task NestedTransactionThrowsInvalidOperation()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Schemas.Product, "Tx");
        var inquiry = harness.GetRequiredService<IInquiry>();

        await using var outer = await inquiry.BeginTransactionAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => outer.Inquiry.BeginTransactionAsync());
    }
}
