using System.Data;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Order;
using Dapper;
using Inquiry.Entities;
using Inquiry.Stores;
using Microsoft.Data.Sqlite;

namespace Inquiry.Benchmarks;

/// <summary>
/// Small <c>IN</c> lists (#509). The generated store binds a list up to the dialect threshold as padded
/// scalar parameters, and a longer list through the collection transport. The two ADO.NET legs run the same
/// query and reader with each transport, so the threshold can be chosen from measurements.
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
[Orderer(SummaryOrderPolicy.Declared, MethodOrderPolicy.Declared)]
public class InListThresholdBenchmarks
{
    private const string SelectSql = "SELECT ShipperID, CompanyName, Phone FROM Shippers";
    private const string TransportSql = SelectSql + " WHERE ShipperID IN (SELECT value FROM json_each($ids))";

    private BenchmarkDatabase _db = null!;
    private InListShipperStore _store = null!;
    private int[] _ids = null!;
    private string[] _names = null!;
    private string _scalarSql = null!;

    [Params(1, 3, 8, 10, 16, 32, 100, 1000)]
    public int ListSize { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        _db = BenchmarkDatabase.CreateAsync(1000).GetAwaiter().GetResult();
        _store = new InListShipperStore(_db.Inquiry);
        _ids = Enumerable.Range(0, ListSize).Select(static i => (i * 7 % 1000) + 1).ToArray();
        _names = Enumerable.Range(0, ListSize).Select(static i => "p" + i.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        _scalarSql = SelectSql + " WHERE ShipperID IN (" + string.Join(", ", _names.Select(static name => "$" + name)) + ")";
    }

    [GlobalCleanup]
    public void GlobalCleanup()
        => _db.DisposeAsync().AsTask().GetAwaiter().GetResult();

    private static InListShipper ReadShipper(System.Data.Common.DbDataReader reader) => new InListShipper
    {
        ShipperID = reader.GetInt32(0),
        CompanyName = reader.GetString(1),
        Phone = reader.IsDBNull(2) ? null : reader.GetString(2),
    };

    private static async Task<int> ReadAllAsync(System.Data.Common.DbCommand command)
    {
        var list = new List<InListShipper>();
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleResult | CommandBehavior.SequentialAccess);
        while (await reader.ReadAsync())
        {
            list.Add(ReadShipper(reader));
        }

        return list.Count;
    }

    private static string Json(int[] ids) => "[" + string.Join(",", ids) + "]";

    /// <summary>One scalar parameter per element, the shape Dapper sends.</summary>
    [BenchmarkCategory("InList"), Benchmark(Baseline = true)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "S2077", Justification = "The IN list holds generated parameter names only; the values are bound as parameters.")]
    public async Task<int> Scalar_AdoNet()
    {
        await using var connection = new SqliteConnection(_db.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = _scalarSql;
        for (var i = 0; i < _ids.Length; i++)
        {
            command.Parameters.Add(new SqliteParameter(_names[i], SqliteType.Integer) { Value = _ids[i] });
        }

        return await ReadAllAsync(command);
    }

    /// <summary>The collection transport Inquiry binds above the threshold.</summary>
    [BenchmarkCategory("InList"), Benchmark]
    public async Task<int> Transport_AdoNet()
    {
        await using var connection = new SqliteConnection(_db.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = TransportSql;
        command.Parameters.Add(new SqliteParameter("$ids", SqliteType.Text) { Value = Json(_ids) });
        return await ReadAllAsync(command);
    }

    [BenchmarkCategory("InList"), Benchmark]
    public async Task<int> Dapper()
    {
        await using var connection = new SqliteConnection(_db.ConnectionString);
        await connection.OpenAsync();
        return (await connection.QueryAsync<InListShipper>(SelectSql + " WHERE ShipperID IN @ids", new { ids = _ids })).AsList().Count;
    }

    [BenchmarkCategory("InList"), Benchmark]
    public async Task<int> Inquiry()
        => (await _store.ByIdsAsync(_ids)).Count;
}

/// <summary>A by-ids lookup over the seeded <c>Shippers</c> rows.</summary>
public partial class InListShipperStore : InquiryStore<InListShipper>
{
    [InquirySelectAllByPredicate]
    [InquiryWhere("ShipperID", Compare.In)]
    public partial Task<IReadOnlyList<InListShipper>> ByIdsAsync(IReadOnlyList<int> ids, CancellationToken cancellationToken = default);
}

/// <summary>The seeded <c>Shippers</c> table, mapped here because the Northwind models compile in another assembly.</summary>
[InquiryTable("Shippers")]
public sealed class InListShipper
{
    [InquiryKey("ShipperID", IsGenerated = true)]
    public int? ShipperID { get; set; }

    [InquiryColumn]
    public string CompanyName { get; set; } = string.Empty;

    [InquiryColumn]
    public string? Phone { get; set; }
}
