using System.Data;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Dapper;
using Inquiry.Northwind.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Inquiry.Benchmarks;

/// <summary>
/// Predicate (filtered-read) comparison over the <c>Products</c> table:
/// <list type="bullet">
///   <item><b>Search</b> — a two-clause AND predicate <c>UnitPrice &gt;= @min AND ProductName LIKE
///   @pattern</c> (Inquiry <c>[InquirySelectAllByPredicate]</c> with two <c>[InquiryWhere]</c>).</item>
///   <item><b>InList</b> — <c>CategoryID IN (...)</c> (Inquiry <c>[InquiryWhere(In)]</c>). Dapper
///   expands the list automatically; the ADO baseline expands positional parameters by hand.</item>
/// </list>
/// ADO/Dapper read the same Product column list the Inquiry materializer reads. EF Core is included
/// as a natural one-liner for both predicates, using a non-pooled context factory (same lifecycle as
/// the CRUD benchmarks).
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class PredicateBenchmarks
{
    private BenchmarkDatabase _db = null!;
    private string _connectionString = null!;

    /// <summary>Seeded row count: the small (1 000) and large (100 000) dataset tiers.</summary>
    [Params(1000, 100000)] public int Rows;

    private const decimal MinPrice    = 20m;
    private const string  NamePattern = "Product 1%";
    private static readonly int[] CategoryIds = { 1, 2, 3 };

    [GlobalSetup]
    public void GlobalSetup()
    {
        _db = BenchmarkDatabase.CreateAsync(Rows).GetAwaiter().GetResult();
        _connectionString = _db.ConnectionString;
    }

    [GlobalCleanup]
    public void GlobalCleanup() => _db.DisposeAsync().AsTask().GetAwaiter().GetResult();

    // Same Product column list ProductCrudBenchmarks reads, so AdoNet/Dapper do equal per-row work.
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

    // ---- Search (UnitPrice >= @min AND ProductName LIKE @pattern) ------------------------

    private const string SearchSql =
        "SELECT " + SelectColumns + " FROM Products WHERE UnitPrice >= $min AND ProductName LIKE $pattern;";
    private const string SearchSqlAt =
        "SELECT " + SelectColumns + " FROM Products WHERE UnitPrice >= @min AND ProductName LIKE @pattern;";

    [BenchmarkCategory("Search"), Benchmark(Baseline = true)]
    public async Task<int> Search_AdoNet()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = SearchSql;
        command.Parameters.Add("$min",     SqliteType.Real).Value = (double)MinPrice;
        command.Parameters.Add("$pattern", SqliteType.Text).Value = NamePattern;
        var list = new List<Product>();
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleResult | CommandBehavior.SequentialAccess);
        while (await reader.ReadAsync()) list.Add(ReadProduct(reader));
        return list.Count;
    }

    [BenchmarkCategory("Search"), Benchmark]
    public async Task<int> Search_Dapper()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        var list = (await connection.QueryAsync<Product>(
            SearchSqlAt, new { min = MinPrice, pattern = NamePattern })).AsList();
        return list.Count;
    }

    [BenchmarkCategory("Search"), Benchmark]
    public async Task<int> Search_EfCore()
    {
        await using var ctx = await _db.DbContextFactory.CreateDbContextAsync();
        var list = await ctx.Products.AsNoTracking()
            .Where(p => p.UnitPrice >= MinPrice && EF.Functions.Like(p.ProductName, NamePattern))
            .ToListAsync();
        return list.Count;
    }

    [BenchmarkCategory("Search"), Benchmark]
    public async Task<int> Search_Inquiry()
    {
        var list = await _db.Products.SearchAsync(MinPrice, NamePattern);
        return list.Count;
    }

    // ---- InList (CategoryID IN (...)) ---------------------------------------------------

    [BenchmarkCategory("InList"), Benchmark(Baseline = true)]
    public async Task<int> InList_AdoNet()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        // Expand the IN list to positional parameters by hand (no driver list support).
        var names = new string[CategoryIds.Length];
        for (int i = 0; i < CategoryIds.Length; i++)
        {
            names[i] = "$c" + i;
            command.Parameters.Add(names[i], SqliteType.Integer).Value = CategoryIds[i];
        }
        command.CommandText =
            $"SELECT {SelectColumns} FROM Products WHERE CategoryID IN ({string.Join(", ", names)});";
        var list = new List<Product>();
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleResult | CommandBehavior.SequentialAccess);
        while (await reader.ReadAsync()) list.Add(ReadProduct(reader));
        return list.Count;
    }

    [BenchmarkCategory("InList"), Benchmark]
    public async Task<int> InList_Dapper()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        // Dapper expands a list parameter into the IN clause automatically.
        var list = (await connection.QueryAsync<Product>(
            $"SELECT {SelectColumns} FROM Products WHERE CategoryID IN @ids;",
            new { ids = CategoryIds })).AsList();
        return list.Count;
    }

    // EF maps CategoryID as int?, so the Contains collection must be int? too.
    private static readonly int?[] CategoryIdsNullable = CategoryIds.Select(x => (int?)x).ToArray();

    [BenchmarkCategory("InList"), Benchmark]
    public async Task<int> InList_EfCore()
    {
        await using var ctx = await _db.DbContextFactory.CreateDbContextAsync();
        var list = await ctx.Products.AsNoTracking()
            .Where(p => CategoryIdsNullable.Contains(p.CategoryID))
            .ToListAsync();
        return list.Count;
    }

    [BenchmarkCategory("InList"), Benchmark]
    public async Task<int> InList_Inquiry()
    {
        var list = await _db.Products.InCategoriesAsync(CategoryIds);
        return list.Count;
    }
}
