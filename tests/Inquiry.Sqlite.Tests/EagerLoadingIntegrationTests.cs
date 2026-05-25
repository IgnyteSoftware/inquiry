using Inquiry.Sqlite.Tests.Fixtures;

namespace Inquiry.Sqlite.Tests;

public sealed class EagerLoadingIntegrationTests
{
    [Fact]
    public async Task SelectOneByKeyEagerLoadsChildCollection()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Schemas.CategoryAndProduct, "Eager");
        var catStore = harness.GetRequiredService<CategoryStore>();
        var prodStore = harness.GetRequiredService<ProductStore>();

        var category = new Category { Key = Guid.NewGuid(), Name = "Electronics" };
        await catStore.InsertAsync(category);

        var products = new[]
        {
            new Product { Key = Guid.NewGuid(), Name = "Phone", Price = 699m, CategoryKey = category.Key },
            new Product { Key = Guid.NewGuid(), Name = "Tablet", Price = 499m, CategoryKey = category.Key },
        };
        foreach (var p in products) await prodStore.InsertAsync(p);

        var loaded = await catStore.SelectByKeyWithProductsAsync(category.Key);

        Assert.NotNull(loaded);
        Assert.Equal("Electronics", loaded.Name);
        Assert.NotNull(loaded.Products);
        Assert.Equal(2, loaded.Products.Count);
        Assert.Contains(loaded.Products, p => p.Name == "Phone");
        Assert.Contains(loaded.Products, p => p.Name == "Tablet");
    }

    [Fact]
    public async Task SelectOneByKeyEagerReturnsNullForMissingEntity()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Schemas.CategoryAndProduct, "Eager");
        var catStore = harness.GetRequiredService<CategoryStore>();

        var loaded = await catStore.SelectByKeyWithProductsAsync(Guid.NewGuid());
        Assert.Null(loaded);
    }

    [Fact]
    public async Task SelectOneByKeyEagerReturnsEmptyCollectionWhenNoChildren()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Schemas.CategoryAndProduct, "Eager");
        var catStore = harness.GetRequiredService<CategoryStore>();

        var category = new Category { Key = Guid.NewGuid(), Name = "Empty Category" };
        await catStore.InsertAsync(category);

        var loaded = await catStore.SelectByKeyWithProductsAsync(category.Key);

        Assert.NotNull(loaded);
        Assert.NotNull(loaded.Products);
        Assert.Empty(loaded.Products);
    }

    [Fact]
    public async Task SelectAllEagerPopulatesChildrenForAllParents()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Schemas.CategoryAndProduct, "Eager");
        var catStore = harness.GetRequiredService<CategoryStore>();
        var prodStore = harness.GetRequiredService<ProductStore>();

        var cat1 = new Category { Key = Guid.NewGuid(), Name = "Cat A" };
        var cat2 = new Category { Key = Guid.NewGuid(), Name = "Cat B" };
        await catStore.InsertAsync(cat1);
        await catStore.InsertAsync(cat2);

        foreach (var p in new[]
        {
            new Product { Key = Guid.NewGuid(), Name = "P1", Price = 1m, CategoryKey = cat1.Key },
            new Product { Key = Guid.NewGuid(), Name = "P2", Price = 2m, CategoryKey = cat1.Key },
            new Product { Key = Guid.NewGuid(), Name = "P3", Price = 3m, CategoryKey = cat2.Key },
        })
        {
            await prodStore.InsertAsync(p);
        }

        var all = await catStore.SelectAllWithProductsAsync().ToListAsync();

        Assert.Equal(2, all.Count);
        var a = all.Single(c => c.Name == "Cat A");
        var b = all.Single(c => c.Name == "Cat B");
        Assert.Equal(2, a.Products?.Count);
        Assert.Equal(1, b.Products?.Count);
    }

    [Fact]
    public async Task SelectOneByKeyEagerLoadsParentReference()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Schemas.CategoryAndProduct, "Eager");
        var catStore = harness.GetRequiredService<CategoryStore>();
        var prodStore = harness.GetRequiredService<ProductStore>();

        var category = new Category { Key = Guid.NewGuid(), Name = "Electronics" };
        await catStore.InsertAsync(category);

        var product = new Product { Key = Guid.NewGuid(), Name = "Phone", Price = 699m, CategoryKey = category.Key };
        await prodStore.InsertAsync(product);

        var loaded = await prodStore.SelectByKeyWithCategoryAsync(product.Key);

        Assert.NotNull(loaded);
        Assert.Equal("Phone", loaded.Name);
        Assert.NotNull(loaded.Category);
        Assert.Equal(category.Key, loaded.Category!.Key);
        Assert.Equal("Electronics", loaded.Category.Name);
    }

    [Fact]
    public async Task SelectAllEagerPopulatesParentForAllChildren()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Schemas.CategoryAndProduct, "Eager");
        var catStore = harness.GetRequiredService<CategoryStore>();
        var prodStore = harness.GetRequiredService<ProductStore>();

        var cat1 = new Category { Key = Guid.NewGuid(), Name = "Cat A" };
        var cat2 = new Category { Key = Guid.NewGuid(), Name = "Cat B" };
        await catStore.InsertAsync(cat1);
        await catStore.InsertAsync(cat2);

        foreach (var p in new[]
        {
            new Product { Key = Guid.NewGuid(), Name = "P1", Price = 1m, CategoryKey = cat1.Key },
            new Product { Key = Guid.NewGuid(), Name = "P2", Price = 2m, CategoryKey = cat1.Key },
            new Product { Key = Guid.NewGuid(), Name = "P3", Price = 3m, CategoryKey = cat2.Key },
        })
        {
            await prodStore.InsertAsync(p);
        }

        var all = await prodStore.SelectAllWithCategoryAsync().ToListAsync();

        Assert.Equal(3, all.Count);
        Assert.All(all, p => Assert.NotNull(p.Category));
        Assert.Equal("Cat A", all.Single(p => p.Name == "P1").Category!.Name);
        Assert.Equal("Cat A", all.Single(p => p.Name == "P2").Category!.Name);
        Assert.Equal("Cat B", all.Single(p => p.Name == "P3").Category!.Name);
    }

    [Fact]
    public async Task SelectByKeyEagerLeavesParentNullForOrphanForeignKey()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Schemas.CategoryAndProduct, "Eager");
        var prodStore = harness.GetRequiredService<ProductStore>();

        // Insert a product whose CategoryKey points at a category that doesn't exist.
        var product = new Product { Key = Guid.NewGuid(), Name = "Orphan", Price = 1m, CategoryKey = Guid.NewGuid() };
        await prodStore.InsertAsync(product);

        var loaded = await prodStore.SelectByKeyWithCategoryAsync(product.Key);

        Assert.NotNull(loaded);
        Assert.Null(loaded.Category);
    }

    [Fact]
    public async Task SelectAllEagerLeavesParentNullForOrphanForeignKey()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Schemas.CategoryAndProduct, "Eager");
        var catStore = harness.GetRequiredService<CategoryStore>();
        var prodStore = harness.GetRequiredService<ProductStore>();

        var category = new Category { Key = Guid.NewGuid(), Name = "Existing" };
        await catStore.InsertAsync(category);

        var matched = new Product { Key = Guid.NewGuid(), Name = "Matched", Price = 1m, CategoryKey = category.Key };
        var orphan = new Product { Key = Guid.NewGuid(), Name = "Orphan", Price = 2m, CategoryKey = Guid.NewGuid() };
        await prodStore.InsertAsync(matched);
        await prodStore.InsertAsync(orphan);

        var all = await prodStore.SelectAllWithCategoryAsync().ToListAsync();

        Assert.Equal(2, all.Count);
        Assert.Equal("Existing", all.Single(p => p.Name == "Matched").Category?.Name);
        Assert.Null(all.Single(p => p.Name == "Orphan").Category);
    }
}
