using Inquiry.DependencyInjection;
using Inquiry.Sqlite.DependencyInjection;
using Inquiry.Sqlite.Tests.Fixtures;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Inquiry.Sqlite.Tests;

public sealed class EagerLoadingIntegrationTests
{
    [Fact]
    public async Task SelectOneByKeyEagerLoadsChildCollection()
    {
        var (sp, keeper) = await SetupAsync();
        await using var _ = keeper;
        using var _sp = sp;
        var catStore = sp.GetRequiredService<CategoryStore>();
        var prodStore = sp.GetRequiredService<ProductStore>();

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
        var (sp, keeper) = await SetupAsync();
        await using var _ = keeper;
        using var _sp = sp;
        var catStore = sp.GetRequiredService<CategoryStore>();

        var loaded = await catStore.SelectByKeyWithProductsAsync(Guid.NewGuid());
        Assert.Null(loaded);
    }

    [Fact]
    public async Task SelectOneByKeyEagerReturnsEmptyCollectionWhenNoChildren()
    {
        var (sp, keeper) = await SetupAsync();
        await using var _ = keeper;
        using var _sp = sp;
        var catStore = sp.GetRequiredService<CategoryStore>();

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
        var (sp, keeper) = await SetupAsync();
        await using var _ = keeper;
        using var _sp = sp;
        var catStore = sp.GetRequiredService<CategoryStore>();
        var prodStore = sp.GetRequiredService<ProductStore>();

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

        var all = await ToListAsync(catStore.SelectAllWithProductsAsync());

        Assert.Equal(2, all.Count);
        var a = all.Single(c => c.Name == "Cat A");
        var b = all.Single(c => c.Name == "Cat B");
        Assert.Equal(2, a.Products?.Count);
        Assert.Equal(1, b.Products?.Count);
    }

    private static async Task<(ServiceProvider sp, SqliteConnection keeper)> SetupAsync()
    {
        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = "Eager_" + Guid.NewGuid().ToString("N"),
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared,
        }.ToString();

        var keeper = new SqliteConnection(cs);
        await keeper.OpenAsync();
        await CreateSchemaAsync(keeper);

        var sp = new ServiceCollection()
            .AddInquiry()
            .AddInquirySqlite(cs)
            .BuildServiceProvider();

        return (sp, keeper);
    }

    private static async Task CreateSchemaAsync(SqliteConnection connection)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE TCategory (
                Key TEXT PRIMARY KEY,
                Name TEXT NOT NULL
            );
            CREATE TABLE TProduct (
                Key TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                Price REAL NOT NULL,
                CategoryKey TEXT NOT NULL
            );
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<List<T>> ToListAsync<T>(IAsyncEnumerable<T> source)
    {
        var list = new List<T>();
        await foreach (var item in source) list.Add(item);
        return list;
    }
}
