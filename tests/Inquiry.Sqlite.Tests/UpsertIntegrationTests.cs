using Inquiry.Sqlite.Tests.Fixtures;

namespace Inquiry.Sqlite.Tests;

public sealed class UpsertIntegrationTests
{
    [Fact]
    public async Task UpsertInsertsWhenRowDoesNotExist()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Schemas.Product, "Upsert");
        var store = harness.GetRequiredService<ProductStore>();

        var product = new Product { Key = Guid.NewGuid(), Name = "New Widget", Price = 19.99m, CategoryKey = Guid.Empty };
        var rows = await store.UpsertAsync(product);

        Assert.Equal(1, rows);
        var loaded = await store.SelectByKeyAsync(product.Key);
        Assert.NotNull(loaded);
        Assert.Equal("New Widget", loaded.Name);
    }

    [Fact]
    public async Task UpsertUpdatesExistingRow()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Schemas.Product, "Upsert");
        var store = harness.GetRequiredService<ProductStore>();

        var product = new Product { Key = Guid.NewGuid(), Name = "Original", Price = 5m, CategoryKey = Guid.Empty };
        await store.InsertAsync(product);

        product.Name = "Updated via Upsert";
        product.Price = 15m;
        await store.UpsertAsync(product);

        var loaded = await store.SelectByKeyAsync(product.Key);
        Assert.NotNull(loaded);
        Assert.Equal("Updated via Upsert", loaded.Name);
        Assert.Equal(15m, loaded.Price);
    }
}
