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
/// Concurrent point-read stress test: <see cref="Concurrency"/> tasks each perform
/// a <c>SELECT … WHERE ShipperID = @id</c> against the same SQLite database file. Measures
/// framework overhead under contention — connection-pool saturation, internal locking,
/// and per-call allocation when multiple callers race.
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class ConcurrencyBenchmarks
{
    private BenchmarkDatabase _db = null!;
    private string _connectionString = null!;
    private DataOptions _linqToDbOptions = null!;

    [Params(4, 16, 64)] public int Concurrency;

    private const int TargetShipperId = 1;

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

    // ---- ConcurrentPointRead ---------------------------------------------------------------

    [BenchmarkCategory("ConcurrentPointRead"), Benchmark(Baseline = true)]
    public async Task<int> ConcurrentPointRead_AdoNet()
    {
        var tasks = new Task<Shipper?>[Concurrency];
        for (int i = 0; i < Concurrency; i++)
        {
            tasks[i] = Task.Run(async () =>
            {
                await using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT ShipperID, CompanyName, Phone FROM Shippers WHERE ShipperID = $id;";
                command.Parameters.Add("$id", SqliteType.Integer).Value = TargetShipperId;
                await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleResult | CommandBehavior.SequentialAccess | CommandBehavior.SingleRow);
                return await reader.ReadAsync() ? ReadShipper(reader) : null;
            });
        }
        var results = await Task.WhenAll(tasks);
        return results.Count(r => r is not null);
    }

    [BenchmarkCategory("ConcurrentPointRead"), Benchmark]
    public async Task<int> ConcurrentPointRead_Dapper()
    {
        var tasks = new Task<Shipper?>[Concurrency];
        for (int i = 0; i < Concurrency; i++)
        {
            tasks[i] = Task.Run(async () =>
            {
                await using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                return await connection.QueryFirstOrDefaultAsync<Shipper>(
                    "SELECT ShipperID, CompanyName, Phone FROM Shippers WHERE ShipperID = @id;",
                    new { id = TargetShipperId });
            });
        }
        var results = await Task.WhenAll(tasks);
        return results.Count(r => r is not null);
    }

    [BenchmarkCategory("ConcurrentPointRead"), Benchmark]
    public async Task<int> ConcurrentPointRead_EfCore()
    {
        var tasks = new Task<EfShipper?>[Concurrency];
        for (int i = 0; i < Concurrency; i++)
            tasks[i] = Task.Run(EfCorePointRead);
        var results = await Task.WhenAll(tasks);
        return results.Count(r => r is not null);
    }

    private async Task<EfShipper?> EfCorePointRead()
    {
        await using var ctx = await _db.DbContextFactory.CreateDbContextAsync();
        return await ctx.Shippers.AsNoTracking().FirstOrDefaultAsync(s => s.ShipperID == TargetShipperId);
    }

    [BenchmarkCategory("ConcurrentPointRead"), Benchmark]
    public async Task<int> ConcurrentPointRead_LinqToDb()
    {
        var tasks = new Task<L2Shipper?>[Concurrency];
        for (int i = 0; i < Concurrency; i++)
            tasks[i] = Task.Run(LinqToDbPointRead);
        var results = await Task.WhenAll(tasks);
        return results.Count(r => r is not null);
    }

    private async Task<L2Shipper?> LinqToDbPointRead()
    {
        await using var dc = new DataConnection(_linqToDbOptions);
        return await dc.GetTable<L2Shipper>().FirstOrDefaultAsync(s => s.ShipperID == TargetShipperId);
    }

    [BenchmarkCategory("ConcurrentPointRead"), Benchmark]
    public async Task<int> ConcurrentPointRead_RepoDb()
    {
        var tasks = new Task<RdShipper?>[Concurrency];
        for (int i = 0; i < Concurrency; i++)
        {
            tasks[i] = Task.Run(async () =>
            {
                await using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                var result = await RepoDb.DbConnectionExtension.QueryAsync<RdShipper>(
                    connection, TargetShipperId);
                return result.FirstOrDefault();
            });
        }
        var results = await Task.WhenAll(tasks);
        return results.Count(r => r is not null);
    }

    [BenchmarkCategory("ConcurrentPointRead"), Benchmark]
    public async Task<int> ConcurrentPointRead_Inquiry()
    {
        var tasks = new Task<Shipper?>[Concurrency];
        for (int i = 0; i < Concurrency; i++)
        {
            tasks[i] = Task.Run(async () => await _db.Shippers.SelectByKeyAsync(TargetShipperId));
        }
        var results = await Task.WhenAll(tasks);
        return results.Count(r => r is not null);
    }
}
