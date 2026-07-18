using System.Data;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Dapper;
using Inquiry.Northwind.Models;
using Microsoft.Data.Sqlite;

namespace Inquiry.Benchmarks;

/// <summary>
/// Eager-grid collection benchmarks: <c>Region → Territories</c> with parameterized density.
/// <list type="bullet">
///   <item>Sparse (<c>RegionCount=100</c>): many parents, few children each.</item>
///   <item>Dense (<c>RegionCount=4</c>): few parents, many children each.</item>
/// </list>
/// Inquiry uses the single-round-trip grid path (<c>QueryMultipleAsync</c>); ADO.NET and Dapper
/// baselines run two queries then stitch in memory (the separate-query alternative).
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class EagerGridBenchmarks
{
    private BenchmarkDatabase _db = null!;
    private string _connectionString = null!;

    [Params(1000, 100000)] public int Rows;
    [Params(4, 100)] public int RegionCount;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _db = BenchmarkDatabase.CreateAsync(Rows).GetAwaiter().GetResult();
        _db.SeedRegionsAsync(RegionCount, Rows).GetAwaiter().GetResult();
        _connectionString = _db.ConnectionString;
    }

    [GlobalCleanup]
    public void GlobalCleanup() => _db.DisposeAsync().AsTask().GetAwaiter().GetResult();

    private const string AllRegionsSql = "SELECT RegionID, RegionDescription FROM Region;";
    private const string AllTerritoriesSql = "SELECT TerritoryID, TerritoryDescription, RegionID FROM Territories;";

    private static Region ReadRegion(System.Data.Common.DbDataReader reader) => new()
    {
        RegionID = reader.GetInt32(0),
        RegionDescription = reader.GetString(1),
    };

    private static Territory ReadTerritory(System.Data.Common.DbDataReader reader) => new()
    {
        TerritoryID = reader.GetString(0),
        TerritoryDescription = reader.GetString(1),
        RegionID = reader.GetInt32(2),
    };

    // ---- EagerGrid (collection: Region → Territories) --------------------------------------

    [BenchmarkCategory("EagerGrid"), Benchmark(Baseline = true)]
    public async Task<int> Grid_AdoNet()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var regionsById = new Dictionary<int, Region>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = AllRegionsSql;
            await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleResult | CommandBehavior.SequentialAccess);
            while (await reader.ReadAsync())
            {
                var region = ReadRegion(reader);
                region.Territories = new List<Territory>();
                regionsById[region.RegionID] = region;
            }
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = AllTerritoriesSql;
            await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleResult | CommandBehavior.SequentialAccess);
            while (await reader.ReadAsync())
            {
                var territory = ReadTerritory(reader);
                if (regionsById.TryGetValue(territory.RegionID, out var region))
                    region.Territories!.Add(territory);
            }
        }

        var count = 0;
        foreach (var region in regionsById.Values) count += region.Territories!.Count;
        return count;
    }

    [BenchmarkCategory("EagerGrid"), Benchmark]
    public async Task<int> Grid_Dapper()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var regions = (await connection.QueryAsync<Region>(AllRegionsSql)).AsList();
        var regionsById = new Dictionary<int, Region>(regions.Count);
        foreach (var region in regions)
        {
            region.Territories = new List<Territory>();
            regionsById[region.RegionID] = region;
        }

        foreach (var territory in await connection.QueryAsync<Territory>(AllTerritoriesSql))
        {
            if (regionsById.TryGetValue(territory.RegionID, out var region))
                region.Territories!.Add(territory);
        }

        var count = 0;
        foreach (var region in regions) count += region.Territories!.Count;
        return count;
    }

    [BenchmarkCategory("EagerGrid"), Benchmark]
    public async Task<int> Grid_Inquiry()
    {
        var count = 0;
        await foreach (var region in _db.Regions.SelectAllWithTerritoriesAsync())
        {
            count += region.Territories?.Count ?? 0;
        }
        return count;
    }

}
