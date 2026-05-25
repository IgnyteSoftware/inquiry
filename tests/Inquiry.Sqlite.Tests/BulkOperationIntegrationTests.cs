using Inquiry.DependencyInjection;
using Inquiry.Sqlite.DependencyInjection;
using Inquiry.Sqlite.Tests.Fixtures;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Inquiry.Sqlite.Tests;

public sealed class BulkOperationIntegrationTests
{
    [Fact]
    public async Task BulkInsertInsertsAllEntities()
    {
        var (cs, sp, keeper) = await SetupAsync();
        await using var _ = keeper;
        using var _sp = sp;
        var store = sp.GetRequiredService<ProductStore>();

        var products = MakeProducts(5, Guid.Empty);
        var count = await store.BulkInsertAsync(products);
        var all = await ToListAsync(store.SelectAllAsync());

        Assert.Equal(5, count);
        Assert.Equal(5, all.Count);
    }

    [Fact]
    public async Task BulkUpdateUpdatesAllEntities()
    {
        var (cs, sp, keeper) = await SetupAsync();
        await using var _ = keeper;
        using var _sp = sp;
        var store = sp.GetRequiredService<ProductStore>();

        var products = MakeProducts(3, Guid.Empty);
        await store.BulkInsertAsync(products);

        foreach (var p in products)
        {
            p.Name += " Updated";
            p.Price *= 2;
        }

        var updated = await store.BulkUpdateAsync(products);
        Assert.Equal(3, updated);

        foreach (var p in products)
        {
            var loaded = await store.SelectByKeyAsync(p.Key);
            Assert.NotNull(loaded);
            Assert.EndsWith(" Updated", loaded.Name);
        }
    }

    [Fact]
    public async Task BulkDeleteDeletesAllKeys()
    {
        var (cs, sp, keeper) = await SetupAsync();
        await using var _ = keeper;
        using var _sp = sp;
        var store = sp.GetRequiredService<ProductStore>();

        var products = MakeProducts(4, Guid.Empty);
        await store.BulkInsertAsync(products);

        var deleted = await store.BulkDeleteAsync(products.Select(p => p.Key));
        Assert.Equal(4, deleted);

        var all = await ToListAsync(store.SelectAllAsync());
        Assert.Empty(all);
    }

    [Fact]
    public async Task BulkInsertWithEmptyCollectionReturnsZero()
    {
        var (cs, sp, keeper) = await SetupAsync();
        await using var _ = keeper;
        using var _sp = sp;
        var store = sp.GetRequiredService<ProductStore>();

        var count = await store.BulkInsertAsync(Enumerable.Empty<Product>());
        Assert.Equal(0, count);
    }

    private static async Task<(string cs, ServiceProvider sp, SqliteConnection keeper)> SetupAsync()
    {
        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = "Bulk_" + Guid.NewGuid().ToString("N"),
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

        return (cs, sp, keeper);
    }

    private static async Task CreateSchemaAsync(SqliteConnection connection)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE TProduct (
                Key TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                Price REAL NOT NULL,
                CategoryKey TEXT NOT NULL
            );
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    private static List<Product> MakeProducts(int count, Guid categoryKey)
    {
        return Enumerable.Range(1, count).Select(i => new Product
        {
            Key = Guid.NewGuid(),
            Name = $"Product {i}",
            Price = i * 5.99m,
            CategoryKey = categoryKey,
        }).ToList();
    }

    private static async Task<List<T>> ToListAsync<T>(IAsyncEnumerable<T> source)
    {
        var list = new List<T>();
        await foreach (var item in source) list.Add(item);
        return list;
    }
}
