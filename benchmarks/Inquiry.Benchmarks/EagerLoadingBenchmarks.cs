using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Dapper;
using Inquiry.Northwind.Models;
using Microsoft.Data.Sqlite;

namespace Inquiry.Benchmarks;

/// <summary>
/// Eager-loading comparison: materialize every <c>Product</c> with its parent <c>Category</c>
/// populated. Inquiry's <c>[InquirySelectAllEager]</c> uses the separate-query strategy (one query
/// for products, one for the related categories, stitched by the generated materializer). To keep
/// the comparison apples-to-apples, the Dapper and ADO baselines run the same two-query-then-stitch
/// pattern: load all products, load all categories into a lookup, assign each product's
/// <see cref="Product.Category"/> in memory.
/// </summary>
/// <remarks>
/// <c>SelectAllWithCategoryAsync</c> returns an <see cref="IAsyncEnumerable{T}"/>, so it is drained
/// with a manual <c>await foreach</c> into a list and the count returned. EF Core is omitted: its
/// eager load is <c>Include(...)</c> which, depending on the version, emits a JOIN or split query —
/// not a like-for-like match to the explicit separate-query stitch measured here.
/// </remarks>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class EagerLoadingBenchmarks
{
    private BenchmarkDatabase _db = null!;
    private string _connectionString = null!;

    /// <summary>Seeded row count: the small (1 000) and large (100 000) dataset tiers.</summary>
    [Params(1000, 100000)] public int Rows;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _db = BenchmarkDatabase.CreateAsync(Rows).GetAwaiter().GetResult();
        _connectionString = _db.ConnectionString;
    }

    [GlobalCleanup]
    public void GlobalCleanup() => _db.DisposeAsync().AsTask().GetAwaiter().GetResult();

    // Same Product column list ProductCrudBenchmarks reads, so AdoNet/Dapper do equal per-row work.
    private const string ProductColumns =
        "ProductID, ProductName, SupplierID, CategoryID, QuantityPerUnit, UnitPrice, UnitsInStock, UnitsOnOrder, ReorderLevel, Discontinued";
    private const string ProductsSql = "SELECT " + ProductColumns + " FROM Products;";
    private const string CategoriesSql = "SELECT CategoryID, CategoryName, Description FROM Categories;";

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

    private static Category ReadCategory(System.Data.Common.DbDataReader reader) => new Category
    {
        CategoryID   = reader.GetInt32(0),
        CategoryName = reader.GetString(1),
        Description  = reader.IsDBNull(2) ? null : reader.GetString(2),
    };

    // ---- EagerAll (separate-query stitch) -----------------------------------------------

    [BenchmarkCategory("EagerAll"), Benchmark(Baseline = true)]
    public async Task<int> EagerAll_AdoNet()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        // Query 1: categories → lookup.
        var categoriesById = new Dictionary<int, Category>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = CategoriesSql;
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var category = ReadCategory(reader);
                categoriesById[category.CategoryID!.Value] = category;
            }
        }

        // Query 2: products → stitch.
        var products = new List<Product>(_db.RowCount);
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = ProductsSql;
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var product = ReadProduct(reader);
                if (product.CategoryID is int cid && categoriesById.TryGetValue(cid, out var category))
                {
                    product.Category = category;
                }
                products.Add(product);
            }
        }

        return products.Count;
    }

    [BenchmarkCategory("EagerAll"), Benchmark]
    public async Task<int> EagerAll_Dapper()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var categoriesById = (await connection.QueryAsync<Category>(CategoriesSql))
            .ToDictionary(c => c.CategoryID!.Value);

        var products = (await connection.QueryAsync<Product>(ProductsSql)).AsList();
        foreach (var product in products)
        {
            if (product.CategoryID is int cid && categoriesById.TryGetValue(cid, out var category))
            {
                product.Category = category;
            }
        }

        return products.Count;
    }

    [BenchmarkCategory("EagerAll"), Benchmark]
    public async Task<int> EagerAll_Inquiry()
    {
        var products = new List<Product>(_db.RowCount);
        await foreach (var product in _db.Products.SelectAllWithCategoryAsync())
        {
            products.Add(product);
        }
        return products.Count;
    }
}
