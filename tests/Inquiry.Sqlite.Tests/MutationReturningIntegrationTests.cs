using Inquiry.Sqlite.Tests.Fixtures;

namespace Inquiry.Sqlite.Tests;

public sealed class MutationReturningIntegrationTests
{
    [Fact]
    public async Task InsertReturningReturnsInsertedRow()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Schemas.Product, "Returning");
        var store = harness.GetRequiredService<ProductStore>();
        var product = new Product { Key = Guid.NewGuid(), Name = "Returned Insert", Price = 12.34m, CategoryKey = Guid.Empty };

        var returned = await store.InsertReturningAsync(product);

        Assert.NotNull(returned);
        Assert.Equal(product.Key, returned.Key);
        Assert.Equal("Returned Insert", returned.Name);
        Assert.Equal(12.34m, returned.Price);
    }

    [Fact]
    public async Task UpdateReturningReturnsUpdatedRowOrNullWhenMissing()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Schemas.Product, "Returning");
        var store = harness.GetRequiredService<ProductStore>();
        var product = new Product { Key = Guid.NewGuid(), Name = "Original", Price = 10m, CategoryKey = Guid.Empty };
        await store.InsertAsync(product);

        product.Name = "Returned Update";
        product.Price = 20m;
        var returned = await store.UpdateReturningAsync(product);
        var missing = await store.UpdateReturningAsync(new Product { Key = Guid.NewGuid(), Name = "Missing", Price = 1m, CategoryKey = Guid.Empty });

        Assert.NotNull(returned);
        Assert.Equal(product.Key, returned.Key);
        Assert.Equal("Returned Update", returned.Name);
        Assert.Equal(20m, returned.Price);
        Assert.Null(missing);
    }

    [Fact]
    public async Task UpsertReturningReturnsInsertedOrUpdatedRow()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Schemas.Product, "Returning");
        var store = harness.GetRequiredService<ProductStore>();
        var product = new Product { Key = Guid.NewGuid(), Name = "Returned Upsert Insert", Price = 5m, CategoryKey = Guid.Empty };

        var inserted = await store.UpsertReturningAsync(product);
        product.Name = "Returned Upsert Update";
        product.Price = 15m;
        var updated = await store.UpsertReturningAsync(product);

        Assert.NotNull(inserted);
        Assert.Equal("Returned Upsert Insert", inserted.Name);
        Assert.NotNull(updated);
        Assert.Equal(product.Key, updated.Key);
        Assert.Equal("Returned Upsert Update", updated.Name);
        Assert.Equal(15m, updated.Price);
    }
}
