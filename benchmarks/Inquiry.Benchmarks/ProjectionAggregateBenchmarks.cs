using System.Data;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Dapper;
using Inquiry.Northwind.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Inquiry.Benchmarks;

/// <summary>
/// Server-side projection and aggregate comparison over the <c>Products</c> table:
/// <list type="bullet">
///   <item><b>Projection</b> — materialize a 3-column subset (ProductID / ProductName / UnitPrice)
///   instead of the full entity. Inquiry uses the <c>[InquiryProjection]</c> <c>ProductSummary</c>
///   record; ADO/Dapper select the same three columns into a small record.</item>
///   <item><b>Count</b> — <c>COUNT(*)</c> (Inquiry <c>[InquiryCount]</c>).</item>
///   <item><b>Sum</b> — <c>SUM(UnitPrice)</c> (Inquiry <c>[InquiryAggregate(Sum)]</c>).</item>
///   <item><b>Avg / Min / Max</b> — <c>AVG / MIN / MAX(UnitPrice)</c>. Inquiry has a generated
///   method for Max only; no generated Avg/Min method exists on ProductStore.</item>
/// </list>
/// EF Core covers Projection and Count here. It is omitted from the decimal aggregates (Sum / Avg /
/// Min / Max) on SQLite because EF Core's SQLite provider cannot translate a decimal aggregate (it
/// throws); those EF arms are included on the networked dialects, which have real decimal types.
/// EF uses a non-pooled context factory, the same lifecycle as the CRUD benchmarks.
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
    public async Task<int> Projection_EfCore()
    {
        await using var ctx = await _db.DbContextFactory.CreateDbContextAsync();
        var list = await ctx.Products.AsNoTracking()
            .Select(p => new ProductSummaryRow(p.ProductID, p.ProductName, p.UnitPrice))
            .ToListAsync();
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
    public async Task<long> Count_EfCore()
    {
        await using var ctx = await _db.DbContextFactory.CreateDbContextAsync();
        return await ctx.Products.AsNoTracking().CountAsync();
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

    // note: EF Core's SQLite provider cannot translate a decimal aggregate (UnitPrice is decimal;
    // SqliteQueryableAggregateMethodTranslator throws). The EF Sum arm is omitted on SQLite — it is
    // included on the networked dialects (PostgreSQL/MySQL/SQL Server have real decimal types).
    [BenchmarkCategory("Sum"), Benchmark]
    public async Task<decimal?> Sum_Inquiry() => await _db.Products.SumUnitPriceAsync();

    // ---- Avg (AVG(UnitPrice)) -----------------------------------------------------------

    private const string AvgSql = "SELECT AVG(UnitPrice) FROM Products;";

    [BenchmarkCategory("Avg"), Benchmark(Baseline = true)]
    public async Task<decimal?> Avg_AdoNet()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = AvgSql;
        var scalar = await command.ExecuteScalarAsync();
        return scalar is null or DBNull ? null : Convert.ToDecimal(scalar);
    }

    [BenchmarkCategory("Avg"), Benchmark]
    public async Task<double?> Avg_Dapper()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        return await connection.ExecuteScalarAsync<double?>(AvgSql);
    }

    // note: EF Core SQLite cannot translate a decimal aggregate, so the EF Avg arm is omitted on
    // SQLite (included on the networked dialects). ProductStore also has no generated
    // [InquiryAggregate(Avg)] method — so on SQLite this category is ADO + Dapper only.

    // ---- Min (MIN(UnitPrice)) -----------------------------------------------------------

    private const string MinSql = "SELECT MIN(UnitPrice) FROM Products;";

    [BenchmarkCategory("Min"), Benchmark(Baseline = true)]
    public async Task<decimal?> Min_AdoNet()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = MinSql;
        var scalar = await command.ExecuteScalarAsync();
        return scalar is null or DBNull ? null : Convert.ToDecimal(scalar);
    }

    [BenchmarkCategory("Min"), Benchmark]
    public async Task<decimal?> Min_Dapper()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        return await connection.ExecuteScalarAsync<decimal?>(MinSql);
    }

    // note: EF Core SQLite cannot translate a decimal aggregate, so the EF Min arm is omitted on
    // SQLite (included on the networked dialects). ProductStore also has no generated
    // [InquiryAggregate(Min)] method — so on SQLite this category is ADO + Dapper only.

    // ---- Max (MAX(UnitPrice)) -----------------------------------------------------------

    private const string MaxSql = "SELECT MAX(UnitPrice) FROM Products;";

    [BenchmarkCategory("Max"), Benchmark(Baseline = true)]
    public async Task<decimal?> Max_AdoNet()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = MaxSql;
        var scalar = await command.ExecuteScalarAsync();
        return scalar is null or DBNull ? null : Convert.ToDecimal(scalar);
    }

    [BenchmarkCategory("Max"), Benchmark]
    public async Task<decimal?> Max_Dapper()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        return await connection.ExecuteScalarAsync<decimal?>(MaxSql);
    }

    // note: EF Core SQLite cannot translate a decimal aggregate, so the EF Max arm is omitted on
    // SQLite (included on the networked dialects).
    [BenchmarkCategory("Max"), Benchmark]
    public async Task<decimal?> Max_Inquiry() => await _db.Products.MaxUnitPriceAsync();
}
