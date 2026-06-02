using System.Data;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Dapper;
using Inquiry.Northwind.Models;
using Microsoft.Data.Sqlite;

namespace Inquiry.Benchmarks;

/// <summary>
/// Pagination comparison over the IDENTITY-keyed <c>Products</c> table. Two strategies are
/// exercised against a deep page (offset ≈ <c>Rows / 2</c>):
/// <list type="bullet">
///   <item><b>OffsetPage</b> — <c>LIMIT/OFFSET</c> ordered by ProductID (Inquiry
///   <c>[InquirySelectAll(Paged = true)]</c>).</item>
///   <item><b>KeysetPage</b> — seek by <c>ProductID &gt; @after</c> (Inquiry
///   <c>[InquiryKeysetPage]</c>, which fetches <c>pageSize + 1</c> to compute the cursor).</item>
/// </list>
/// ADO/Dapper read the same Product column list the Inquiry materializer reads, so per-row work
/// is equal. EF Core is omitted: it has no first-class keyset helper, so a LINQ <c>Skip/Take</c>
/// would only mirror the offset path and not the keyset one.
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class PaginationBenchmarks
{
    private BenchmarkDatabase _db = null!;
    private string _connectionString = null!;

    /// <summary>Seeded row count: the small (1 000) and large (100 000) dataset tiers.</summary>
    [Params(1000, 100000)] public int Rows;

    private const int PageSize = 20;
    private int _offset;
    private int _after;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _db = BenchmarkDatabase.CreateAsync(Rows).GetAwaiter().GetResult();
        _connectionString = _db.ConnectionString;
        _offset = Rows / 2;   // deep page so OFFSET cost is realistic
        _after  = Rows / 2;   // keyset seek point
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

    // ---- OffsetPage (LIMIT / OFFSET) ----------------------------------------------------

    private const string OffsetSql =
        "SELECT " + SelectColumns + " FROM Products ORDER BY ProductID LIMIT $limit OFFSET $off;";
    private const string OffsetSqlAt =
        "SELECT " + SelectColumns + " FROM Products ORDER BY ProductID LIMIT @limit OFFSET @off;";

    [BenchmarkCategory("OffsetPage"), Benchmark(Baseline = true)]
    public async Task<int> OffsetPage_AdoNet()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = OffsetSql;
        command.Parameters.Add("$limit", SqliteType.Integer).Value = PageSize;
        command.Parameters.Add("$off",   SqliteType.Integer).Value = _offset;
        var list = new List<Product>(PageSize);
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleResult);
        while (await reader.ReadAsync()) list.Add(ReadProduct(reader));
        return list.Count;
    }

    [BenchmarkCategory("OffsetPage"), Benchmark]
    public async Task<int> OffsetPage_Dapper()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        var list = (await connection.QueryAsync<Product>(
            OffsetSqlAt, new { limit = PageSize, off = _offset })).AsList();
        return list.Count;
    }

    [BenchmarkCategory("OffsetPage"), Benchmark]
    public async Task<int> OffsetPage_Inquiry()
    {
        var list = await _db.Products.PageByIdAsync(offset: _offset, limit: PageSize);
        return list.Count;
    }

    // ---- KeysetPage (seek by ProductID > @after) ----------------------------------------
    // Inquiry fetches pageSize + 1 to derive the cursor, so ADO/Dapper request the same +1
    // for a fair row-count comparison.

    private const string KeysetSql =
        "SELECT " + SelectColumns + " FROM Products WHERE ProductID > $after ORDER BY ProductID LIMIT $limit;";
    private const string KeysetSqlAt =
        "SELECT " + SelectColumns + " FROM Products WHERE ProductID > @after ORDER BY ProductID LIMIT @limit;";

    [BenchmarkCategory("KeysetPage"), Benchmark(Baseline = true)]
    public async Task<int> KeysetPage_AdoNet()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = KeysetSql;
        command.Parameters.Add("$after", SqliteType.Integer).Value = _after;
        command.Parameters.Add("$limit", SqliteType.Integer).Value = PageSize + 1;
        var list = new List<Product>(PageSize + 1);
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleResult);
        while (await reader.ReadAsync()) list.Add(ReadProduct(reader));
        return list.Count;
    }

    [BenchmarkCategory("KeysetPage"), Benchmark]
    public async Task<int> KeysetPage_Dapper()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        var list = (await connection.QueryAsync<Product>(
            KeysetSqlAt, new { after = _after, limit = PageSize + 1 })).AsList();
        return list.Count;
    }

    [BenchmarkCategory("KeysetPage"), Benchmark]
    public async Task<int> KeysetPage_Inquiry()
    {
        var page = await _db.Products.KeysetByIdAsync(afterProductID: _after, pageSize: PageSize);
        return page.Items.Count;
    }
}
