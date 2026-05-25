using Inquiry.DependencyInjection;
using Inquiry.Sqlite.DependencyInjection;
using Inquiry.Sqlite.Tests.Fixtures;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Inquiry.Sqlite.Tests;

public sealed class UpsertIntegrationTests
{
    [Fact]
    public async Task UpsertInsertsWhenRowDoesNotExist()
    {
        var (sp, keeper) = await SetupAsync();
        await using var _ = keeper;
        using var _sp = sp;
        var store = sp.GetRequiredService<ProductStore>();

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
        var (sp, keeper) = await SetupAsync();
        await using var _ = keeper;
        using var _sp = sp;
        var store = sp.GetRequiredService<ProductStore>();

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

    private static async Task<(ServiceProvider sp, SqliteConnection keeper)> SetupAsync()
    {
        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = "Upsert_" + Guid.NewGuid().ToString("N"),
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
            CREATE TABLE TProduct (
                Key TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                Price REAL NOT NULL,
                CategoryKey TEXT NOT NULL
            );
            """;
        await cmd.ExecuteNonQueryAsync();
    }
}
