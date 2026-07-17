using System.Data;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Dapper;
using Inquiry.Benchmarks.LinqToDb;
using Inquiry.Commands;
using Inquiry.Northwind.Models;
using LinqToDB;
using LinqToDB.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Inquiry.Benchmarks;

/// <summary>
/// Buffered vs streaming reads over the <c>Products</c> table. ADO.NET always streams via
/// <see cref="System.Data.Common.DbDataReader"/>; the "buffered" path collects into a list while
/// the "streaming" path consumes rows one at a time via async enumeration. Dapper has no native
/// <see cref="IAsyncEnumerable{T}"/> path and is therefore omitted from <c>StreamingRead</c>.
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class StreamingBenchmarks
{
    private BenchmarkDatabase _db = null!;
    private string _connectionString = null!;
    private DataOptions _linqToDbOptions = null!;

    [Params(1000, 100000)] public int Rows;

    private const string SelectColumns =
        "ProductID, ProductName, SupplierID, CategoryID, QuantityPerUnit, UnitPrice, UnitsInStock, UnitsOnOrder, ReorderLevel, Discontinued";

    private static readonly string SelectAllSql = $"SELECT {SelectColumns} FROM Products;";

    [GlobalSetup]
    public void GlobalSetup()
    {
        _db = BenchmarkDatabase.CreateAsync(Rows).GetAwaiter().GetResult();
        _connectionString = _db.ConnectionString;
        _linqToDbOptions = _db.LinqToDbOptions;
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

    // ---- BufferedRead -------------------------------------------------------------------

    [BenchmarkCategory("BufferedRead"), Benchmark(Baseline = true)]
    public async Task<int> BufferedRead_AdoNet()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = SelectAllSql;
        var list = new List<Product>(_db.RowCount);
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleResult | CommandBehavior.SequentialAccess);
        while (await reader.ReadAsync()) list.Add(ReadProduct(reader));
        return list.Count;
    }

    [BenchmarkCategory("BufferedRead"), Benchmark]
    public async Task<int> BufferedRead_Dapper()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        var list = (await connection.QueryAsync<Product>(SelectAllSql)).AsList();
        return list.Count;
    }

    [BenchmarkCategory("BufferedRead"), Benchmark]
    public async Task<int> BufferedRead_LinqToDb()
    {
        await using var dc = new DataConnection(_linqToDbOptions);
        var list = await dc.GetTable<L2Product>().ToListAsync();
        return list.Count;
    }

    [BenchmarkCategory("BufferedRead"), Benchmark]
    public async Task<int> BufferedRead_Inquiry()
    {
        var list = await _db.Inquiry.QueryListAsync<Product>(new InquiryCommand(SelectAllSql));
        return list.Count;
    }

    // ---- StreamingRead ------------------------------------------------------------------

    [BenchmarkCategory("StreamingRead"), Benchmark(Baseline = true)]
    public async Task<int> StreamingRead_AdoNet()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = SelectAllSql;
        var count = 0;
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleResult | CommandBehavior.SequentialAccess);
        while (await reader.ReadAsync()) { ReadProduct(reader); count++; }
        return count;
    }

    [BenchmarkCategory("StreamingRead"), Benchmark]
    public async Task<int> StreamingRead_LinqToDb()
    {
        await using var dc = new DataConnection(_linqToDbOptions);
        var count = 0;
        await foreach (var _ in dc.GetTable<L2Product>().AsAsyncEnumerable())
            count++;
        return count;
    }

    [BenchmarkCategory("StreamingRead"), Benchmark]
    public async Task<int> StreamingRead_Inquiry()
    {
        var count = 0;
        await foreach (var _ in _db.Inquiry.QueryAsync<Product>(new InquiryCommand(SelectAllSql)))
            count++;
        return count;
    }
}
