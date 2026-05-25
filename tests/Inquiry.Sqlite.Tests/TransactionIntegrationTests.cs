using Inquiry.DependencyInjection;
using Inquiry.Sqlite.DependencyInjection;
using Inquiry.Sqlite.Tests.Fixtures;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Inquiry.Sqlite.Tests;

public sealed class TransactionIntegrationTests
{
    [Fact]
    public async Task CommittedTransactionPersistsChanges()
    {
        var cs = CreateConnectionString();
        await using var keeper = new SqliteConnection(cs);
        await keeper.OpenAsync();
        await CreateProductSchemaAsync(keeper);

        using var sp = BuildServiceProvider(cs);
        var inquiry = sp.GetRequiredService<IInquiry>();
        var store = sp.GetRequiredService<ProductStore>();

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
        var cs = CreateConnectionString();
        await using var keeper = new SqliteConnection(cs);
        await keeper.OpenAsync();
        await CreateProductSchemaAsync(keeper);

        using var sp = BuildServiceProvider(cs);
        var inquiry = sp.GetRequiredService<IInquiry>();
        var store = sp.GetRequiredService<ProductStore>();

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
        var cs = CreateConnectionString();
        await using var keeper = new SqliteConnection(cs);
        await keeper.OpenAsync();
        await CreateProductSchemaAsync(keeper);

        using var sp = BuildServiceProvider(cs);
        var inquiry = sp.GetRequiredService<IInquiry>();
        var store = sp.GetRequiredService<ProductStore>();

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
        var cs = CreateConnectionString();
        await using var keeper = new SqliteConnection(cs);
        await keeper.OpenAsync();
        await CreateProductSchemaAsync(keeper);

        using var sp = BuildServiceProvider(cs);
        var inquiry = sp.GetRequiredService<IInquiry>();
        var store = sp.GetRequiredService<ProductStore>();

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

        var all = await ToListAsync(store.SelectAllAsync());
        Assert.Equal(5, all.Count);
    }

    private static ServiceProvider BuildServiceProvider(string cs)
    {
        return new ServiceCollection()
            .AddInquiry()
            .AddInquirySqlite(cs)
            .BuildServiceProvider();
    }

    private static string CreateConnectionString()
    {
        return new SqliteConnectionStringBuilder
        {
            DataSource = "TxTest_" + Guid.NewGuid().ToString("N"),
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared,
        }.ToString();
    }

    private static async Task CreateProductSchemaAsync(SqliteConnection connection)
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

    private static async Task<List<T>> ToListAsync<T>(IAsyncEnumerable<T> source)
    {
        var list = new List<T>();
        await foreach (var item in source) list.Add(item);
        return list;
    }
}
