using System.Data;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using DlgLib = global::Inquiry.Benchmarks.DLG;

namespace Inquiry.Benchmarks.SqlServer;

/// <summary>
/// Read comparison over the Northwind <c>Products</c> table against SQL Server — Count, offset
/// pagination, and a LIKE search — across ADO.NET (baseline), Dapper, EF Core, Inquiry, and DLG.
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class ProductReadBenchmarks
{
    private SqlServerBenchmarkDatabase _db = null!;

    [Params(1000)]
    public int Rows;

    private const int PageOffset = 20;
    private const int PageSize   = 20;
    private const string NamePattern = "%Product 1%";

    [GlobalSetup]
    public void GlobalSetup() => _db = SqlServerBenchmarkDatabase.CreateAsync(Rows).GetAwaiter().GetResult();

    [GlobalCleanup]
    public void GlobalCleanup() => _db.DisposeAsync().AsTask().GetAwaiter().GetResult();

    private SqlConnection OpenConnection() => new SqlConnection(_db.ConnectionString);

    // ---- Count --------------------------------------------------------------------------

    [BenchmarkCategory("Count"), Benchmark(Baseline = true)]
    public async Task<long> Count_AdoNet()
    {
        await using var connection = OpenConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT_BIG(*) FROM [Products]";
        return (long)(await command.ExecuteScalarAsync())!;
    }

    [BenchmarkCategory("Count"), Benchmark]
    public async Task<long> Count_Dapper()
    {
        await using var connection = OpenConnection();
        await connection.OpenAsync();
        return await connection.ExecuteScalarAsync<long>("SELECT COUNT_BIG(*) FROM [Products]");
    }

    [BenchmarkCategory("Count"), Benchmark]
    public async Task<int> Count_EfCore()
    {
        await using var ctx = await _db.ProductContextFactory.CreateDbContextAsync();
        return await ctx.Products.CountAsync();
    }

    [BenchmarkCategory("Count"), Benchmark]
    public async Task<long> Count_Inquiry() => await _db.Products.CountAsync();

    [BenchmarkCategory("Count"), Benchmark]
    public async Task<int> Count_Dlg() => await DlgLib.Product.SelectAllCountAsync();

    // ---- OffsetPage ---------------------------------------------------------------------

    [BenchmarkCategory("OffsetPage"), Benchmark(Baseline = true)]
    public async Task<int> OffsetPage_AdoNet()
    {
        await using var connection = OpenConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT [ProductID] FROM [Products] ORDER BY [ProductID] OFFSET @off ROWS FETCH NEXT @lim ROWS ONLY";
        command.Parameters.AddWithValue("@off", PageOffset);
        command.Parameters.AddWithValue("@lim", PageSize);
        var n = 0;
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleResult | CommandBehavior.SequentialAccess);
        while (await reader.ReadAsync()) n++;
        return n;
    }

    [BenchmarkCategory("OffsetPage"), Benchmark]
    public async Task<int> OffsetPage_Dapper()
    {
        await using var connection = OpenConnection();
        await connection.OpenAsync();
        var list = (await connection.QueryAsync<int>(
            "SELECT [ProductID] FROM [Products] ORDER BY [ProductID] OFFSET @off ROWS FETCH NEXT @lim ROWS ONLY",
            new { off = PageOffset, lim = PageSize })).AsList();
        return list.Count;
    }

    [BenchmarkCategory("OffsetPage"), Benchmark]
    public async Task<int> OffsetPage_EfCore()
    {
        await using var ctx = await _db.ProductContextFactory.CreateDbContextAsync();
        var list = await ctx.Products.AsNoTracking().OrderBy(p => p.ProductID).Skip(PageOffset).Take(PageSize).ToListAsync();
        return list.Count;
    }

    [BenchmarkCategory("OffsetPage"), Benchmark]
    public async Task<int> OffsetPage_Inquiry()
    {
        var list = await _db.Products.PageByIdAsync(PageOffset, PageSize);
        return list.Count;
    }

    [BenchmarkCategory("OffsetPage"), Benchmark]
    public async Task<int> OffsetPage_Dlg()
    {
        // DLG paging is 1-based page numbers: page 2 @ size 20 == offset 20.
        var list = await DlgLib.Product.SelectAllPagedAsync(pageNumber: 2, pageSize: PageSize, orderByStatement: "ProductID");
        return list.Count;
    }

    // ---- Search (LIKE) ------------------------------------------------------------------

    [BenchmarkCategory("Search"), Benchmark(Baseline = true)]
    public async Task<int> Search_AdoNet()
    {
        await using var connection = OpenConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT [ProductID] FROM [Products] WHERE [ProductName] LIKE @p";
        command.Parameters.AddWithValue("@p", NamePattern);
        var n = 0;
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleResult | CommandBehavior.SequentialAccess);
        while (await reader.ReadAsync()) n++;
        return n;
    }

    [BenchmarkCategory("Search"), Benchmark]
    public async Task<int> Search_Dapper()
    {
        await using var connection = OpenConnection();
        await connection.OpenAsync();
        var list = (await connection.QueryAsync<int>(
            "SELECT [ProductID] FROM [Products] WHERE [ProductName] LIKE @p", new { p = NamePattern })).AsList();
        return list.Count;
    }

    [BenchmarkCategory("Search"), Benchmark]
    public async Task<int> Search_EfCore()
    {
        await using var ctx = await _db.ProductContextFactory.CreateDbContextAsync();
        var list = await ctx.Products.AsNoTracking().Where(p => EF.Functions.Like(p.ProductName, NamePattern)).ToListAsync();
        return list.Count;
    }

    [BenchmarkCategory("Search"), Benchmark]
    public async Task<int> Search_Inquiry()
    {
        // SearchAsync ANDs UnitPrice >= minPrice with ProductName LIKE; minPrice 0 makes the price clause a no-op.
        var list = await _db.Products.SearchAsync(0m, NamePattern);
        return list.Count;
    }

    [BenchmarkCategory("Search"), Benchmark]
    public async Task<int> Search_Dlg()
    {
        var list = await DlgLib.Product.SelectByFieldAsync(DlgLib.ProductFields.ProductName, NamePattern, null, DlgLib.TypeOperation.Like);
        return list.Count;
    }
}
