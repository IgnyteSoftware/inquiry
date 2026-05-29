using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Dapper;
using Inquiry.Benchmarks.Ef;
using Inquiry.Northwind.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Inquiry.Benchmarks;

/// <summary>
/// CRUD comparison for the IDENTITY-keyed <c>Products</c> table. Operations: SelectAll,
/// SelectByKey, SelectByField (CategoryID), Insert, Update, Upsert.
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class ProductCrudBenchmarks
{
    private BenchmarkDatabase _db = null!;
    private string _connectionString = null!;

    // Fixed targets — ProductID 1 is the first IDENTITY-seeded row; CategoryID 1 is the
    // first seeded category (Products are evenly distributed across 8 categories).
    private const int TargetProductId = 1;
    private const int TargetCategoryId = 1;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _db = BenchmarkDatabase.CreateAsync().GetAwaiter().GetResult();
        _connectionString = _db.ConnectionString;
    }

    [GlobalCleanup]
    public void GlobalCleanup() => _db.DisposeAsync().AsTask().GetAwaiter().GetResult();

    // Read every column the Inquiry-mapped Product entity reads, so AdoNet/Dapper do equal
    // per-row work to Inquiry (a fair comparison — not a hand-picked 6-of-10 subset).
    private const string SelectColumns =
        "ProductID, ProductName, SupplierID, CategoryID, QuantityPerUnit, UnitPrice, UnitsInStock, UnitsOnOrder, ReorderLevel, Discontinued";

    private static Product ReadProduct(System.Data.Common.DbDataReader reader) => new Product
    {
        ProductID       = reader.GetInt32(0),
        ProductName     = reader.GetString(1),
        SupplierID      = reader.IsDBNull(2) ? null : reader.GetInt32(2),
        CategoryID      = reader.IsDBNull(3) ? null : reader.GetInt32(3),
        QuantityPerUnit = reader.IsDBNull(4) ? null : reader.GetString(4),
        UnitPrice       = reader.IsDBNull(5) ? null : reader.GetDecimal(5),
        UnitsInStock    = reader.IsDBNull(6) ? null : reader.GetInt16(6),
        UnitsOnOrder    = reader.IsDBNull(7) ? null : reader.GetInt16(7),
        ReorderLevel    = reader.IsDBNull(8) ? null : reader.GetInt16(8),
        Discontinued    = reader.GetInt32(9) != 0,
    };

    // ---- SelectAll ----------------------------------------------------------------------

    [BenchmarkCategory("SelectAll"), Benchmark(Baseline = true)]
    public async Task<int> SelectAll_AdoNet()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectColumns} FROM Products;";
        var list = new List<Product>(BenchmarkDatabase.SeedRows);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) list.Add(ReadProduct(reader));
        return list.Count;
    }

    [BenchmarkCategory("SelectAll"), Benchmark]
    public async Task<int> SelectAll_Dapper()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        var list = (await connection.QueryAsync<Product>($"SELECT {SelectColumns} FROM Products;")).AsList();
        return list.Count;
    }

    [BenchmarkCategory("SelectAll"), Benchmark]
    public async Task<int> SelectAll_EfCore()
    {
        await using var ctx = await _db.DbContextFactory.CreateDbContextAsync();
        var list = await ctx.Products.AsNoTracking().ToListAsync();
        return list.Count;
    }

    [BenchmarkCategory("SelectAll"), Benchmark]
    public async Task<int> SelectAll_Inquiry()
    {
        var list = await _db.Products.SelectAllAsync();
        return list.Count;
    }

    // ---- SelectByKey --------------------------------------------------------------------

    [BenchmarkCategory("SelectByKey"), Benchmark(Baseline = true)]
    public async Task<Product?> SelectByKey_AdoNet()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectColumns} FROM Products WHERE ProductID = $id;";
        command.Parameters.Add("$id", SqliteType.Integer).Value = TargetProductId;
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? ReadProduct(reader) : null;
    }

    [BenchmarkCategory("SelectByKey"), Benchmark]
    public async Task<Product?> SelectByKey_Dapper()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        return await connection.QuerySingleOrDefaultAsync<Product>(
            $"SELECT {SelectColumns} FROM Products WHERE ProductID = @id;",
            new { id = TargetProductId });
    }

    [BenchmarkCategory("SelectByKey"), Benchmark]
    public async Task<EfProduct?> SelectByKey_EfCore()
    {
        await using var ctx = await _db.DbContextFactory.CreateDbContextAsync();
        return await ctx.Products.AsNoTracking().FirstOrDefaultAsync(p => p.ProductID == TargetProductId);
    }

    [BenchmarkCategory("SelectByKey"), Benchmark]
    public async Task<Product?> SelectByKey_Inquiry()
        => await _db.Products.SelectByKeyAsync(TargetProductId);

    // ---- SelectByField (CategoryID) -----------------------------------------------------

    [BenchmarkCategory("SelectByField"), Benchmark(Baseline = true)]
    public async Task<int> SelectByField_AdoNet()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectColumns} FROM Products WHERE CategoryID = $c;";
        command.Parameters.Add("$c", SqliteType.Integer).Value = TargetCategoryId;
        var list = new List<Product>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) list.Add(ReadProduct(reader));
        return list.Count;
    }

    [BenchmarkCategory("SelectByField"), Benchmark]
    public async Task<int> SelectByField_Dapper()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        var list = (await connection.QueryAsync<Product>(
            $"SELECT {SelectColumns} FROM Products WHERE CategoryID = @c;",
            new { c = TargetCategoryId })).AsList();
        return list.Count;
    }

    [BenchmarkCategory("SelectByField"), Benchmark]
    public async Task<int> SelectByField_EfCore()
    {
        await using var ctx = await _db.DbContextFactory.CreateDbContextAsync();
        var list = await ctx.Products.AsNoTracking().Where(p => p.CategoryID == TargetCategoryId).ToListAsync();
        return list.Count;
    }

    [BenchmarkCategory("SelectByField"), Benchmark]
    public async Task<int> SelectByField_Inquiry()
    {
        var list = await _db.Products.SelectByCategoryAsync(TargetCategoryId);
        return list.Count;
    }

    // ---- Insert -------------------------------------------------------------------------

    [BenchmarkCategory("Insert"), Benchmark(Baseline = true)]
    public async Task<int> Insert_AdoNet()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO Products (ProductName, CategoryID, UnitPrice, UnitsInStock, Discontinued) " +
            "VALUES ($name, $category, $price, $stock, 0);";
        command.Parameters.Add("$name",     SqliteType.Text).Value    = "Bench Product";
        command.Parameters.Add("$category", SqliteType.Integer).Value = TargetCategoryId;
        command.Parameters.Add("$price",    SqliteType.Real).Value    = 9.99;
        command.Parameters.Add("$stock",    SqliteType.Integer).Value = 42;
        return await command.ExecuteNonQueryAsync();
    }

    [BenchmarkCategory("Insert"), Benchmark]
    public async Task<int> Insert_Dapper()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        return await connection.ExecuteAsync(
            "INSERT INTO Products (ProductName, CategoryID, UnitPrice, UnitsInStock, Discontinued) " +
            "VALUES (@name, @category, @price, @stock, 0);",
            new { name = "Bench Product", category = TargetCategoryId, price = 9.99m, stock = (short)42 });
    }

    [BenchmarkCategory("Insert"), Benchmark]
    public async Task<int> Insert_EfCore()
    {
        await using var ctx = await _db.DbContextFactory.CreateDbContextAsync();
        ctx.Products.Add(new EfProduct
        {
            ProductName  = "Bench Product",
            CategoryID   = TargetCategoryId,
            UnitPrice    = 9.99m,
            UnitsInStock = 42,
            Discontinued = false,
        });
        return await ctx.SaveChangesAsync();
    }

    [BenchmarkCategory("Insert"), Benchmark]
    public async Task<int> Insert_Inquiry()
        => await _db.Products.InsertAsync(new Product
        {
            ProductName  = "Bench Product",
            CategoryID   = TargetCategoryId,
            UnitPrice    = 9.99m,
            UnitsInStock = 42,
            Discontinued = false,
        });

    // ---- Update -------------------------------------------------------------------------

    [BenchmarkCategory("Update"), Benchmark(Baseline = true)]
    public async Task<int> Update_AdoNet()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "UPDATE Products SET ProductName = $name, CategoryID = $category, " +
            "UnitPrice = $price, UnitsInStock = $stock, Discontinued = 0 WHERE ProductID = $id;";
        command.Parameters.Add("$id",       SqliteType.Integer).Value = TargetProductId;
        command.Parameters.Add("$name",     SqliteType.Text).Value    = "Updated Product";
        command.Parameters.Add("$category", SqliteType.Integer).Value = TargetCategoryId;
        command.Parameters.Add("$price",    SqliteType.Real).Value    = 19.99;
        command.Parameters.Add("$stock",    SqliteType.Integer).Value = 7;
        return await command.ExecuteNonQueryAsync();
    }

    [BenchmarkCategory("Update"), Benchmark]
    public async Task<int> Update_Dapper()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        return await connection.ExecuteAsync(
            "UPDATE Products SET ProductName = @name, CategoryID = @category, " +
            "UnitPrice = @price, UnitsInStock = @stock, Discontinued = 0 WHERE ProductID = @id;",
            new { id = TargetProductId, name = "Updated Product", category = TargetCategoryId, price = 19.99m, stock = (short)7 });
    }

    [BenchmarkCategory("Update"), Benchmark]
    public async Task<int> Update_EfCore()
    {
        await using var ctx = await _db.DbContextFactory.CreateDbContextAsync();
        var entity = await ctx.Products.FirstAsync(p => p.ProductID == TargetProductId);
        entity.ProductName  = "Updated Product";
        entity.CategoryID   = TargetCategoryId;
        entity.UnitPrice    = 19.99m;
        entity.UnitsInStock = 7;
        entity.Discontinued = false;
        return await ctx.SaveChangesAsync();
    }

    [BenchmarkCategory("Update"), Benchmark]
    public async Task<bool> Update_Inquiry()
        => await _db.Products.UpdateAsync(new Product
        {
            ProductID    = TargetProductId,
            ProductName  = "Updated Product",
            CategoryID   = TargetCategoryId,
            UnitPrice    = 19.99m,
            UnitsInStock = 7,
            Discontinued = false,
        });

    // ---- Upsert -------------------------------------------------------------------------

    [BenchmarkCategory("Upsert"), Benchmark(Baseline = true)]
    public async Task<int> Upsert_AdoNet()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO Products (ProductID, ProductName, CategoryID, UnitPrice, UnitsInStock, Discontinued) " +
            "VALUES ($id, $name, $category, $price, $stock, 0) " +
            "ON CONFLICT(ProductID) DO UPDATE SET " +
            "    ProductName  = excluded.ProductName, " +
            "    CategoryID   = excluded.CategoryID, " +
            "    UnitPrice    = excluded.UnitPrice, " +
            "    UnitsInStock = excluded.UnitsInStock, " +
            "    Discontinued = excluded.Discontinued;";
        command.Parameters.Add("$id",       SqliteType.Integer).Value = TargetProductId;
        command.Parameters.Add("$name",     SqliteType.Text).Value    = "Upserted Product";
        command.Parameters.Add("$category", SqliteType.Integer).Value = TargetCategoryId;
        command.Parameters.Add("$price",    SqliteType.Real).Value    = 29.99;
        command.Parameters.Add("$stock",    SqliteType.Integer).Value = 3;
        return await command.ExecuteNonQueryAsync();
    }

    [BenchmarkCategory("Upsert"), Benchmark]
    public async Task<int> Upsert_Dapper()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        return await connection.ExecuteAsync(
            "INSERT INTO Products (ProductID, ProductName, CategoryID, UnitPrice, UnitsInStock, Discontinued) " +
            "VALUES (@id, @name, @category, @price, @stock, 0) " +
            "ON CONFLICT(ProductID) DO UPDATE SET " +
            "    ProductName  = excluded.ProductName, " +
            "    CategoryID   = excluded.CategoryID, " +
            "    UnitPrice    = excluded.UnitPrice, " +
            "    UnitsInStock = excluded.UnitsInStock, " +
            "    Discontinued = excluded.Discontinued;",
            new { id = TargetProductId, name = "Upserted Product", category = TargetCategoryId, price = 29.99m, stock = (short)3 });
    }

    [BenchmarkCategory("Upsert"), Benchmark]
    public async Task<int> Upsert_EfCore()
    {
        await using var ctx = await _db.DbContextFactory.CreateDbContextAsync();
        return await ctx.Database.ExecuteSqlRawAsync(
            "INSERT INTO Products (ProductID, ProductName, CategoryID, UnitPrice, UnitsInStock, Discontinued) " +
            "VALUES ({0}, {1}, {2}, {3}, {4}, 0) " +
            "ON CONFLICT(ProductID) DO UPDATE SET " +
            "    ProductName  = excluded.ProductName, " +
            "    CategoryID   = excluded.CategoryID, " +
            "    UnitPrice    = excluded.UnitPrice, " +
            "    UnitsInStock = excluded.UnitsInStock, " +
            "    Discontinued = excluded.Discontinued;",
            TargetProductId, "Upserted Product", TargetCategoryId, 29.99m, (short)3);
    }

    [BenchmarkCategory("Upsert"), Benchmark]
    public async Task<int> Upsert_Inquiry()
        => await _db.Products.UpsertAsync(new Product
        {
            ProductID    = TargetProductId,
            ProductName  = "Upserted Product",
            CategoryID   = TargetCategoryId,
            UnitPrice    = 29.99m,
            UnitsInStock = 3,
            Discontinued = false,
        });
}
