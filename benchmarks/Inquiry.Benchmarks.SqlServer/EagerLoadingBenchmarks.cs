using System.Data;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Dapper;
using Microsoft.Data.SqlClient;
using DlgLib = global::Inquiry.Benchmarks.DLG;

namespace Inquiry.Benchmarks.SqlServer;

/// <summary>
/// Eager parent-with-children: load one <c>Category</c> together with its <c>Products</c> in a single
/// round-trip — the shape DLG supports natively (<c>SelectOneWithProductsUsingCategoryID</c>). Legs:
/// ADO.NET (baseline, two result sets), Dapper (multi-result), Inquiry (generated eager), DLG.
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class EagerLoadingBenchmarks
{
    private SqlServerBenchmarkDatabase _db = null!;

    [Params(1000)]
    public int Rows;

    // First category id under the benchmark seed. Categories are seeded first (10 rows), so id 1 exists.
    private const int TargetCategoryId = 1;

    [GlobalSetup]
    public void GlobalSetup() => _db = SqlServerBenchmarkDatabase.CreateAsync(Rows).GetAwaiter().GetResult();

    [GlobalCleanup]
    public void GlobalCleanup() => _db.DisposeAsync().AsTask().GetAwaiter().GetResult();

    private SqlConnection OpenConnection() => new SqlConnection(_db.ConnectionString);

    [BenchmarkCategory("EagerParentChildren"), Benchmark(Baseline = true)]
    public async Task<int> Eager_AdoNet()
    {
        await using var connection = OpenConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT [CategoryID], [CategoryName] FROM [Categories] WHERE [CategoryID] = @id; " +
            "SELECT [ProductID] FROM [Products] WHERE [CategoryID] = @id;";
        command.Parameters.AddWithValue("@id", TargetCategoryId);
        await using var reader = await command.ExecuteReaderAsync();
        var hasCategory = await reader.ReadAsync();
        await reader.NextResultAsync();
        var childCount = 0;
        while (await reader.ReadAsync()) childCount++;
        return hasCategory ? childCount : -1;
    }

    [BenchmarkCategory("EagerParentChildren"), Benchmark]
    public async Task<int> Eager_Dapper()
    {
        await using var connection = OpenConnection();
        await connection.OpenAsync();
        using var multi = await connection.QueryMultipleAsync(
            "SELECT [CategoryID], [CategoryName] FROM [Categories] WHERE [CategoryID] = @id; " +
            "SELECT [ProductID] FROM [Products] WHERE [CategoryID] = @id;",
            new { id = TargetCategoryId });
        _ = await multi.ReadFirstOrDefaultAsync<(int, string)>();
        var children = (await multi.ReadAsync<int>()).AsList();
        return children.Count;
    }

    [BenchmarkCategory("EagerParentChildren"), Benchmark]
    public async Task<int> Eager_Inquiry()
    {
        var category = await _db.Categories.SelectByKeyWithProductsAsync(TargetCategoryId);
        return category?.Products?.Count ?? -1;
    }

    [BenchmarkCategory("EagerParentChildren"), Benchmark]
    public async Task<int> Eager_Dlg()
    {
        var category = await DlgLib.Category.SelectOneWithProductsUsingCategoryIDAsync(TargetCategoryId);
        return category?.ProductsUsingCategoryID?.Count ?? -1;
    }
}
