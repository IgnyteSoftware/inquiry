using System.Data;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Inquiry.Commands;
using Inquiry.Northwind.Models;
using Inquiry.Northwind.Stores;
using Microsoft.Data.Sqlite;

namespace Inquiry.Benchmarks;

/// <summary>
/// Point read, non-query and scalar calls with a cancelable caller token (#507). Each category compares the
/// Inquiry call with raw ADO.NET doing the same work with the same token, so <c>Ratio</c> and
/// <c>Alloc Ratio</c> show what Inquiry adds. <see cref="TokenMode"/> picks the token:
/// <c>None</c> passes the default token, <c>Reused</c> one never-cancelled source for the whole run, and
/// <c>Fresh</c> a new source per call, which is the per-request token shape of ASP.NET Core.
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "BenchmarkDotNet disposes these fields in the [GlobalCleanup] method.")]
public class CancelableTokenBenchmarks
{
    public enum CallerTokenMode
    {
        None,
        Reused,
        Fresh,
    }

    private const int TargetShipperId = 1;
    private const string UpdatedCompanyName = "Updated Shipper";
    private const string UpdatedPhone = "555-9999";
    private const string SelectByKeySql = "SELECT ShipperID, CompanyName, Phone FROM Shippers WHERE ShipperID = $id;";
    private const string UpdateSql = "UPDATE Shippers SET CompanyName = $company, Phone = $phone WHERE ShipperID = $id;";
    private const string ScalarSql = "SELECT CompanyName FROM Shippers WHERE ShipperID = 1;";

    private BenchmarkDatabase _db = null!;
    private string _connectionString = null!;
    private ShipperStore _shippers = null!;
    private IInquiry _inquiry = null!;
    private InquiryCommand _scalarCommand = null!;
    private CancellationTokenSource _reused = null!;
    private readonly Shipper _update = new() { ShipperID = TargetShipperId, CompanyName = UpdatedCompanyName, Phone = UpdatedPhone };

    [Params(CallerTokenMode.None, CallerTokenMode.Reused, CallerTokenMode.Fresh)]
    public CallerTokenMode TokenMode { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        _db = BenchmarkDatabase.CreateAsync(1000).GetAwaiter().GetResult();
        _connectionString = _db.ConnectionString;
        _shippers = _db.Shippers;
        _inquiry = _db.Inquiry;
        _scalarCommand = new InquiryCommand(ScalarSql);
        _reused = new CancellationTokenSource();
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _reused.Dispose();
        _db.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private CancellationTokenSource? CreateFreshSource()
        => TokenMode == CallerTokenMode.Fresh ? new CancellationTokenSource() : null;

    private CancellationToken SelectToken(CancellationTokenSource? fresh)
        => fresh?.Token ?? (TokenMode == CallerTokenMode.Reused ? _reused.Token : default);

    private static Shipper ReadShipper(System.Data.Common.DbDataReader reader) => new()
    {
        ShipperID = reader.GetInt32(0),
        CompanyName = reader.GetString(1),
        Phone = reader.IsDBNull(2) ? null : reader.GetString(2),
    };

    // ---- PointRead ----------------------------------------------------------------------

    [BenchmarkCategory("PointRead"), Benchmark(Baseline = true)]
    public async Task<Shipper?> PointRead_AdoNet()
    {
        using var fresh = CreateFreshSource();
        var token = SelectToken(fresh);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = SelectByKeySql;
        command.Parameters.Add("$id", SqliteType.Integer).Value = TargetShipperId;
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleResult | CommandBehavior.SingleRow | CommandBehavior.SequentialAccess, token);
        return await reader.ReadAsync(token) ? ReadShipper(reader) : null;
    }

    [BenchmarkCategory("PointRead"), Benchmark]
    public async Task<Shipper?> PointRead_Inquiry()
    {
        using var fresh = CreateFreshSource();
        return await _shippers.SelectByKeyAsync(TargetShipperId, SelectToken(fresh));
    }

    // ---- NonQuery -----------------------------------------------------------------------

    [BenchmarkCategory("NonQuery"), Benchmark(Baseline = true)]
    public async Task<int> NonQuery_AdoNet()
    {
        using var fresh = CreateFreshSource();
        var token = SelectToken(fresh);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = UpdateSql;
        command.Parameters.Add("$id", SqliteType.Integer).Value = TargetShipperId;
        command.Parameters.Add("$company", SqliteType.Text).Value = UpdatedCompanyName;
        command.Parameters.Add("$phone", SqliteType.Text).Value = UpdatedPhone;
        return await command.ExecuteNonQueryAsync(token);
    }

    [BenchmarkCategory("NonQuery"), Benchmark]
    public async Task<bool> NonQuery_Inquiry()
    {
        using var fresh = CreateFreshSource();
        return await _shippers.UpdateAsync(_update, SelectToken(fresh));
    }

    // ---- Scalar -------------------------------------------------------------------------

    [BenchmarkCategory("Scalar"), Benchmark(Baseline = true)]
    public async Task<string?> Scalar_AdoNet()
    {
        using var fresh = CreateFreshSource();
        var token = SelectToken(fresh);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = ScalarSql;
        return (string?)await command.ExecuteScalarAsync(token);
    }

    [BenchmarkCategory("Scalar"), Benchmark]
    public async Task<string?> Scalar_Inquiry()
    {
        using var fresh = CreateFreshSource();
        return await _inquiry.ExecuteScalarAsync<string?>(_scalarCommand, SelectToken(fresh));
    }
}
