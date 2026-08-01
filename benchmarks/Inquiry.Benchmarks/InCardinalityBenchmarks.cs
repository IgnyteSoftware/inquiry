using System.Data;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Dapper;
using Inquiry.Benchmarks.LinqToDb;
using Inquiry.Northwind.Models;
using LinqToDB;
using LinqToDB.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Inquiry.Benchmarks;

/// <summary>
/// Measures parameter binding cost as the <c>IN (...)</c> list size grows. Each leg runs
/// <c>SELECT … FROM Products WHERE CategoryID IN (…)</c> with <see cref="ListSize"/>
/// elements. Since only 8 distinct CategoryIDs exist in the seed data, larger lists
/// repeat values — that is intentional: the benchmark isolates parameter expansion and
/// binding overhead, not result-set size.
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class InCardinalityBenchmarks
{
    private BenchmarkDatabase _db = null!;
    private string _connectionString = null!;
    private DataOptions _linqToDbOptions = null!;

    [Params(3, 10, 100, 1000)] public int ListSize;

    private int[] _ids = null!;

    private const string SelectColumns =
        "ProductID, ProductName, SupplierID, CategoryID, QuantityPerUnit, UnitPrice, UnitsInStock, UnitsOnOrder, ReorderLevel, Discontinued";

    [GlobalSetup]
    public void GlobalSetup()
    {
        _db = BenchmarkDatabase.CreateAsync().GetAwaiter().GetResult();
        _connectionString = _db.ConnectionString;
        _linqToDbOptions = _db.LinqToDbOptions;
        _ids = Enumerable.Range(0, ListSize).Select(i => (i % 8) + 1).ToArray();
    }

    [GlobalCleanup]
    public void GlobalCleanup() => _db.DisposeAsync().AsTask().GetAwaiter().GetResult();

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

    // ---- InList (parameterized cardinality) -------------------------------------------------

    [BenchmarkCategory("InList"), Benchmark(Baseline = true)]
    public async Task<int> InList_AdoNet()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        var names = new string[_ids.Length];
        for (int i = 0; i < _ids.Length; i++)
        {
            names[i] = "$c" + i;
            command.Parameters.Add(names[i], SqliteType.Integer).Value = _ids[i];
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
        var list = (await connection.QueryAsync<Product>(
            $"SELECT {SelectColumns} FROM Products WHERE CategoryID IN @ids;",
            new { ids = _ids })).AsList();
        return list.Count;
    }

    private int?[] _idsNullable = null!;

    [IterationSetup]
    public void IterationSetup()
    {
        _idsNullable ??= _ids.Select(x => (int?)x).ToArray();
    }

    [BenchmarkCategory("InList"), Benchmark]
    public async Task<int> InList_EfCore()
    {
        await using var ctx = await _db.DbContextFactory.CreateDbContextAsync();
        var list = await ctx.Products.AsNoTracking()
            .Where(p => _idsNullable.Contains(p.CategoryID))
            .ToListAsync();
        return list.Count;
    }

    [BenchmarkCategory("InList"), Benchmark]
    public async Task<int> InList_LinqToDb()
    {
        await using var dc = new DataConnection(_linqToDbOptions);
        var list = await dc.GetTable<L2Product>()
            .Where(p => _ids.Contains(p.CategoryID!.Value))
            .ToListAsync();
        return list.Count;
    }

    [BenchmarkCategory("InList"), Benchmark]
    public async Task<int> InList_Inquiry()
    {
        var list = await _db.Products.InCategoriesAsync(_ids);
        return list.Count;
    }
}
