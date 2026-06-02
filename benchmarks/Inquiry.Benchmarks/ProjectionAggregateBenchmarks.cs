using System.Data;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Dapper;
using Inquiry.Northwind.Models;
using Microsoft.Data.Sqlite;

namespace Inquiry.Benchmarks;

/// <summary>
/// Server-side projection and aggregate comparison over the <c>Products</c> table:
/// <list type="bullet">
///   <item><b>Projection</b> — materialize a 3-column subset (ProductID / ProductName / UnitPrice)
///   instead of the full entity. Inquiry uses the <c>[InquiryProjection]</c> <c>ProductSummary</c>
///   record; ADO/Dapper select the same three columns into a small record.</item>
///   <item><b>Count</b> — <c>COUNT(*)</c> (Inquiry <c>[InquiryCount]</c>).</item>
///   <item><b>Sum</b> — <c>SUM(UnitPrice)</c> (Inquiry <c>[InquiryAggregate(Sum)]</c>).</item>
/// </list>
/// EF Core is omitted — its scalar aggregate / projection paths are a separate comparison and the
/// in-process SQLite suite already pits EF in the CRUD classes.
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class ProjectionAggregateBenchmarks
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

    /// <summary>Mirror of <see cref="ProductSummary"/>'s columns for the ADO / Dapper side.</summary>
    private sealed record ProductSummaryRow(int ProductID, string ProductName, decimal? UnitPrice);

    // ---- Projection (3-column subset) ---------------------------------------------------

    private const string ProjectionSql = "SELECT ProductID, ProductName, UnitPrice FROM Products;";

    [BenchmarkCategory("Projection"), Benchmark(Baseline = true)]
    public async Task<int> Projection_AdoNet()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = ProjectionSql;
        var list = new List<ProductSummaryRow>(_db.RowCount);
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleResult);
        while (await reader.ReadAsync())
        {
            list.Add(new ProductSummaryRow(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetDecimal(2)));
        }
        return list.Count;
    }

    [BenchmarkCategory("Projection"), Benchmark]
    public async Task<int> Projection_Dapper()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        var list = (await connection.QueryAsync<ProductSummaryRow>(ProjectionSql)).AsList();
        return list.Count;
    }

    [BenchmarkCategory("Projection"), Benchmark]
    public async Task<int> Projection_Inquiry()
    {
        var list = await _db.Products.SummariesAsync();
        return list.Count;
    }

    // ---- Count (COUNT(*)) ---------------------------------------------------------------

    private const string CountSql = "SELECT COUNT(*) FROM Products;";

    [BenchmarkCategory("Count"), Benchmark(Baseline = true)]
    public async Task<long> Count_AdoNet()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = CountSql;
        return (long)(await command.ExecuteScalarAsync())!;
    }

    [BenchmarkCategory("Count"), Benchmark]
    public async Task<long> Count_Dapper()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        return await connection.ExecuteScalarAsync<long>(CountSql);
    }

    [BenchmarkCategory("Count"), Benchmark]
    public async Task<long> Count_Inquiry() => await _db.Products.CountAsync();

    // ---- Sum (SUM(UnitPrice)) -----------------------------------------------------------

    private const string SumSql = "SELECT SUM(UnitPrice) FROM Products;";

    [BenchmarkCategory("Sum"), Benchmark(Baseline = true)]
    public async Task<decimal?> Sum_AdoNet()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = SumSql;
        var scalar = await command.ExecuteScalarAsync();
        // SQLite returns SUM over a NUMERIC column as a floating value; normalise to decimal.
        return scalar is null or DBNull ? null : Convert.ToDecimal(scalar);
    }

    [BenchmarkCategory("Sum"), Benchmark]
    public async Task<decimal?> Sum_Dapper()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        return await connection.ExecuteScalarAsync<decimal?>(SumSql);
    }

    [BenchmarkCategory("Sum"), Benchmark]
    public async Task<decimal?> Sum_Inquiry() => await _db.Products.SumUnitPriceAsync();
}
