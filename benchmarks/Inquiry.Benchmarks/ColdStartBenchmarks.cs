using System.Data;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Dapper;
using Inquiry.Benchmarks.Ef;
using Inquiry.Benchmarks.LinqToDb;
using Inquiry.Benchmarks.RepoDB;
using Inquiry.Northwind.Models;
using LinqToDB;
using LinqToDB.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Inquiry.Benchmarks;

/// <summary>
/// First-call cost (cold start) for a point read. Measures JIT compilation, connection pool
/// creation, and framework initialization overhead by running with no warmup — the first
/// iteration captures the full cold-start penalty.
/// </summary>
[WarmupCount(0), IterationCount(3), MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class ColdStartBenchmarks
{
    private BenchmarkDatabase _db = null!;
    private string _connectionString = null!;
    private DataOptions _linqToDbOptions = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _db = BenchmarkDatabase.CreateAsync().GetAwaiter().GetResult();
        _connectionString = _db.ConnectionString;
        _linqToDbOptions = _db.LinqToDbOptions;
    }

    [GlobalCleanup]
    public void GlobalCleanup() => _db.DisposeAsync().AsTask().GetAwaiter().GetResult();

    private static Shipper ReadShipper(System.Data.Common.DbDataReader reader) => new Shipper
    {
        ShipperID   = reader.GetInt32(0),
        CompanyName = reader.GetString(1),
        Phone       = reader.IsDBNull(2) ? null : reader.GetString(2),
    };

    // ---- ColdPointRead ---------------------------------------------------------------------

    [BenchmarkCategory("ColdPointRead"), Benchmark(Baseline = true)]
    public async Task<Shipper?> ColdPointRead_AdoNet()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT ShipperID, CompanyName, Phone FROM Shippers WHERE ShipperID = $id;";
        command.Parameters.Add("$id", SqliteType.Integer).Value = 1;
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleResult | CommandBehavior.SingleRow | CommandBehavior.SequentialAccess);
        return await reader.ReadAsync() ? ReadShipper(reader) : null;
    }

    [BenchmarkCategory("ColdPointRead"), Benchmark]
    public async Task<Shipper?> ColdPointRead_Dapper()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        return await connection.QuerySingleOrDefaultAsync<Shipper>(
            "SELECT ShipperID, CompanyName, Phone FROM Shippers WHERE ShipperID = @id;",
            new { id = 1 });
    }

    [BenchmarkCategory("ColdPointRead"), Benchmark]
    public async Task<EfShipper?> ColdPointRead_EfCore()
    {
        await using var ctx = await _db.DbContextFactory.CreateDbContextAsync();
        return await ctx.Shippers.AsNoTracking().FirstOrDefaultAsync(s => s.ShipperID == 1);
    }

    [BenchmarkCategory("ColdPointRead"), Benchmark]
    public async Task<L2Shipper?> ColdPointRead_LinqToDb()
    {
        await using var dc = new DataConnection(_linqToDbOptions);
        return await dc.GetTable<L2Shipper>().FirstOrDefaultAsync(s => s.ShipperID == 1);
    }

    [BenchmarkCategory("ColdPointRead"), Benchmark]
    public async Task<RdShipper?> ColdPointRead_RepoDb()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        return (await RepoDb.DbConnectionExtension.QueryAsync<RdShipper>(connection, 1)).FirstOrDefault();
    }

    [BenchmarkCategory("ColdPointRead"), Benchmark]
    public async Task<Shipper?> ColdPointRead_Inquiry()
        => await _db.Shippers.SelectByKeyAsync(1);
}
