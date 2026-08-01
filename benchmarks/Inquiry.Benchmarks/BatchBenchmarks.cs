using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Dapper;
using Inquiry.Benchmarks.LinqToDb;
using Inquiry.Benchmarks.RepoDB;
using Inquiry.Northwind.Models;
using LinqToDB;
using LinqToDB.Data;
using Microsoft.Data.Sqlite;

namespace Inquiry.Benchmarks;

/// <summary>
/// Batch-insert comparison: write <see cref="BatchSize"/> <c>Region</c> rows per iteration. Inquiry's
/// <c>[InquiryInsertAll]</c> emits a single multi-row INSERT statement; Dapper and the ADO baseline
/// loop a prepared single-row INSERT inside one transaction. This isolates the cost of one batched
/// round-trip versus N parameterized round-trips against the same engine.
/// </summary>
/// <remarks>
/// Region has an explicit (non-IDENTITY) integer PK, so every iteration must use a fresh RegionID
/// range or it collides with rows a previous iteration inserted. A monotonic counter scaled by
/// <see cref="BatchSize"/> gives each iteration a disjoint <c>[base, base + BatchSize)</c> window —
/// no <c>[IterationSetup]</c> table-clear needed. The dataset-size <c>[Params]</c> is intentionally
/// absent: this is a fixed-size write benchmark, not a read over the seeded data.
/// EF Core is omitted — its <c>AddRange</c> + <c>SaveChanges</c> batches on a different mechanism
/// (parameter batching) and would not be a like-for-like multi-row-INSERT comparison.
/// </remarks>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class BatchBenchmarks
{
    private const int BatchSize = 500;

    private BenchmarkDatabase _db = null!;
    private string _connectionString = null!;
    private DataOptions _linqToDbOptions = null!;

    // Scaled by BatchSize so each iteration's RegionID window is disjoint from every other's.
    private int _iterationCounter;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _db = BenchmarkDatabase.CreateAsync().GetAwaiter().GetResult();
        _connectionString = _db.ConnectionString;
        _linqToDbOptions = _db.LinqToDbOptions;
    }

    [GlobalCleanup]
    public void GlobalCleanup() => _db.DisposeAsync().AsTask().GetAwaiter().GetResult();

    /// <summary>Builds a fresh batch of <see cref="BatchSize"/> regions over a disjoint RegionID window.</summary>
    private List<Region> BuildBatch()
    {
        int baseId = Interlocked.Increment(ref _iterationCounter) * BatchSize;
        var batch = new List<Region>(BatchSize);
        for (int i = 0; i < BatchSize; i++)
        {
            batch.Add(new Region
            {
                RegionID          = baseId + i,
                RegionDescription = "Region " + (baseId + i),
            });
        }
        return batch;
    }

    // ---- BatchInsert --------------------------------------------------------------------

    [BenchmarkCategory("BatchInsert"), Benchmark(Baseline = true)]
    public async Task<int> BatchInsert_AdoNet()
    {
        var batch = BuildBatch();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var tx = (SqliteTransaction)await connection.BeginTransactionAsync();
        await using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = "INSERT INTO Region (RegionID, RegionDescription) VALUES ($id, $desc);";
        var pId   = command.Parameters.Add("$id",   SqliteType.Integer);
        var pDesc = command.Parameters.Add("$desc", SqliteType.Text);
        await command.PrepareAsync();
        int affected = 0;
        foreach (var region in batch)
        {
            pId.Value   = region.RegionID;
            pDesc.Value = region.RegionDescription;
            affected += await command.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync();
        return affected;
    }

    [BenchmarkCategory("BatchInsert"), Benchmark]
    public async Task<int> BatchInsert_Dapper()
    {
        var batch = BuildBatch();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var tx = (SqliteTransaction)await connection.BeginTransactionAsync();
        int affected = 0;
        foreach (var region in batch)
        {
            affected += await connection.ExecuteAsync(
                "INSERT INTO Region (RegionID, RegionDescription) VALUES (@id, @desc);",
                new { id = region.RegionID, desc = region.RegionDescription }, tx);
        }
        await tx.CommitAsync();
        return affected;
    }

    [BenchmarkCategory("BatchInsert"), Benchmark]
    public async Task<int> BatchInsert_LinqToDb()
    {
        var batch = BuildBatch();
        await using var dc = new DataConnection(_linqToDbOptions);
        await using var tx = await dc.BeginTransactionAsync();
        int affected = 0;
        foreach (var region in batch)
            affected += await dc.InsertAsync(new L2Region { RegionID = region.RegionID, RegionDescription = region.RegionDescription });
        await tx.CommitAsync();
        return affected;
    }

    [BenchmarkCategory("BatchInsert"), Benchmark]
    public async Task<int> BatchInsert_RepoDb()
    {
        var batch = BuildBatch();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        var rdBatch = batch.Select(r => new RdRegion { RegionID = r.RegionID, RegionDescription = r.RegionDescription }).ToList();
        return await RepoDb.DbConnectionExtension.InsertAllAsync(connection, rdBatch);
    }

    [BenchmarkCategory("BatchInsert"), Benchmark]
    public async Task<int> BatchInsert_Inquiry()
    {
        var batch = BuildBatch();
        return await _db.Regions.InsertAllAsync(batch);
    }
}
