using System.Data;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Dapper;
using Inquiry.Benchmarks.LinqToDb;
using Inquiry.Benchmarks.RepoDB;
using Inquiry.Northwind.Models;
using LinqToDB;
using LinqToDB.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Inquiry.Benchmarks;

/// <summary>
/// Measures the overhead of wrapping a single-row INSERT in a transaction vs. a bare insert.
/// Uses the <c>Region</c> table (explicit PK, no IDENTITY) with a monotonically incrementing
/// ID so every iteration inserts a unique row without conflicts.
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class TransactionBenchmarks
{
    private BenchmarkDatabase _db = null!;
    private string _connectionString = null!;
    private DataOptions _linqToDbOptions = null!;
    private int _counter;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _db = BenchmarkDatabase.CreateAsync().GetAwaiter().GetResult();
        _connectionString = _db.ConnectionString;
        _linqToDbOptions = _db.LinqToDbOptions;
    }

    [GlobalCleanup]
    public void GlobalCleanup() => _db.DisposeAsync().AsTask().GetAwaiter().GetResult();

    // ---- BareInsert ---------------------------------------------------------------------

    [BenchmarkCategory("BareInsert"), Benchmark(Baseline = true)]
    public async Task<int> BareInsert_AdoNet()
    {
        var id = Interlocked.Increment(ref _counter);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO Region (RegionID, RegionDescription) VALUES ($id, $desc);";
        command.Parameters.Add("$id",   SqliteType.Integer).Value = id;
        command.Parameters.Add("$desc", SqliteType.Text).Value    = "Region " + id;
        return await command.ExecuteNonQueryAsync();
    }

    [BenchmarkCategory("BareInsert"), Benchmark]
    public async Task<int> BareInsert_Dapper()
    {
        var id = Interlocked.Increment(ref _counter);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        return await connection.ExecuteAsync(
            "INSERT INTO Region (RegionID, RegionDescription) VALUES (@id, @desc);",
            new { id, desc = "Region " + id });
    }

    [BenchmarkCategory("BareInsert"), Benchmark]
    public async Task<int> BareInsert_LinqToDb()
    {
        var id = Interlocked.Increment(ref _counter);
        await using var dc = new DataConnection(_linqToDbOptions);
        return await dc.InsertAsync(new L2Region { RegionID = id, RegionDescription = "Region " + id });
    }

    [BenchmarkCategory("BareInsert"), Benchmark]
    public async Task<object?> BareInsert_RepoDb()
    {
        var id = Interlocked.Increment(ref _counter);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        return await RepoDb.DbConnectionExtension.InsertAsync(connection,
            new RdRegion { RegionID = id, RegionDescription = "Region " + id });
    }

    [BenchmarkCategory("BareInsert"), Benchmark]
    public async Task<int> BareInsert_Inquiry()
    {
        var id = Interlocked.Increment(ref _counter);
        return await _db.Regions.InsertAsync(new Region { RegionID = id, RegionDescription = "Region " + id });
    }

    // ---- TransactedInsert ---------------------------------------------------------------

    [BenchmarkCategory("TransactedInsert"), Benchmark(Baseline = true)]
    public async Task<int> TransactedInsert_AdoNet()
    {
        var id = Interlocked.Increment(ref _counter);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)tx;
        command.CommandText = "INSERT INTO Region (RegionID, RegionDescription) VALUES ($id, $desc);";
        command.Parameters.Add("$id",   SqliteType.Integer).Value = id;
        command.Parameters.Add("$desc", SqliteType.Text).Value    = "Region " + id;
        var rows = await command.ExecuteNonQueryAsync();
        await tx.CommitAsync();
        return rows;
    }

    [BenchmarkCategory("TransactedInsert"), Benchmark]
    public async Task<int> TransactedInsert_Dapper()
    {
        var id = Interlocked.Increment(ref _counter);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();
        var rows = await connection.ExecuteAsync(
            "INSERT INTO Region (RegionID, RegionDescription) VALUES (@id, @desc);",
            new { id, desc = "Region " + id },
            transaction: tx);
        await tx.CommitAsync();
        return rows;
    }

    [BenchmarkCategory("TransactedInsert"), Benchmark]
    public async Task<int> TransactedInsert_LinqToDb()
    {
        var id = Interlocked.Increment(ref _counter);
        await using var dc = new DataConnection(_linqToDbOptions);
        await dc.BeginTransactionAsync();
        var rows = await dc.InsertAsync(new L2Region { RegionID = id, RegionDescription = "Region " + id });
        await dc.CommitTransactionAsync();
        return rows;
    }

    [BenchmarkCategory("TransactedInsert"), Benchmark]
    public async Task<object?> TransactedInsert_RepoDb()
    {
        var id = Interlocked.Increment(ref _counter);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();
        var result = await RepoDb.DbConnectionExtension.InsertAsync(connection,
            new RdRegion { RegionID = id, RegionDescription = "Region " + id },
            transaction: tx);
        await tx.CommitAsync();
        return result;
    }

    [BenchmarkCategory("TransactedInsert"), Benchmark]
    public async Task<int> TransactedInsert_Inquiry()
    {
        var id = Interlocked.Increment(ref _counter);
        await using var tx = await _db.Inquiry.BeginTransactionAsync(IsolationLevel.Serializable);
        var rows = await tx.ExecuteAsync($"INSERT INTO Region (RegionID, RegionDescription) VALUES ({id}, {"Region " + id})");
        await tx.CommitAsync();
        return rows;
    }
}
