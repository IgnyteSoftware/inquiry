using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Dapper;
using Inquiry.Benchmarks.Ef;
using Inquiry.Northwind.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Inquiry.Benchmarks;

/// <summary>
/// CRUD comparison for the small IDENTITY-keyed <c>Shippers</c> table — three columns, so
/// per-row materialization is cheap and the framework overhead dominates. Operations:
/// SelectAll, SelectByKey, SelectByField (CompanyName), Insert, Update, Upsert.
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class ShipperCrudBenchmarks
{
    private BenchmarkDatabase _db = null!;
    private string _connectionString = null!;

    /// <summary>Seeded row count: the small (1 000) and large (100 000) dataset tiers.</summary>
    [Params(1000, 100000)] public int Rows;

    private const int TargetShipperId = 1;
    private const string TargetCompanyName = "Shipper 0";

    [GlobalSetup]
    public void GlobalSetup()
    {
        _db = BenchmarkDatabase.CreateAsync(Rows).GetAwaiter().GetResult();
        _connectionString = _db.ConnectionString;
    }

    [GlobalCleanup]
    public void GlobalCleanup() => _db.DisposeAsync().AsTask().GetAwaiter().GetResult();

    private static Shipper ReadShipper(System.Data.Common.DbDataReader reader) => new Shipper
    {
        ShipperID   = reader.GetInt32(0),
        CompanyName = reader.GetString(1),
        Phone       = reader.IsDBNull(2) ? null : reader.GetString(2),
    };

    // ---- SelectAll ----------------------------------------------------------------------

    [BenchmarkCategory("SelectAll"), Benchmark(Baseline = true)]
    public async Task<int> SelectAll_AdoNet()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT ShipperID, CompanyName, Phone FROM Shippers;";
        var list = new List<Shipper>(_db.RowCount);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) list.Add(ReadShipper(reader));
        return list.Count;
    }

    [BenchmarkCategory("SelectAll"), Benchmark]
    public async Task<int> SelectAll_Dapper()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        var list = (await connection.QueryAsync<Shipper>("SELECT ShipperID, CompanyName, Phone FROM Shippers;")).AsList();
        return list.Count;
    }

    [BenchmarkCategory("SelectAll"), Benchmark]
    public async Task<int> SelectAll_EfCore()
    {
        await using var ctx = await _db.DbContextFactory.CreateDbContextAsync();
        var list = await ctx.Shippers.AsNoTracking().ToListAsync();
        return list.Count;
    }

    [BenchmarkCategory("SelectAll"), Benchmark]
    public async Task<int> SelectAll_Inquiry()
    {
        var list = await _db.Shippers.SelectAllAsync();
        return list.Count;
    }

    // ---- SelectByKey --------------------------------------------------------------------

    [BenchmarkCategory("SelectByKey"), Benchmark(Baseline = true)]
    public async Task<Shipper?> SelectByKey_AdoNet()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT ShipperID, CompanyName, Phone FROM Shippers WHERE ShipperID = $id;";
        command.Parameters.Add("$id", SqliteType.Integer).Value = TargetShipperId;
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? ReadShipper(reader) : null;
    }

    [BenchmarkCategory("SelectByKey"), Benchmark]
    public async Task<Shipper?> SelectByKey_Dapper()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        return await connection.QuerySingleOrDefaultAsync<Shipper>(
            "SELECT ShipperID, CompanyName, Phone FROM Shippers WHERE ShipperID = @id;",
            new { id = TargetShipperId });
    }

    [BenchmarkCategory("SelectByKey"), Benchmark]
    public async Task<EfShipper?> SelectByKey_EfCore()
    {
        await using var ctx = await _db.DbContextFactory.CreateDbContextAsync();
        return await ctx.Shippers.AsNoTracking().FirstOrDefaultAsync(s => s.ShipperID == TargetShipperId);
    }

    [BenchmarkCategory("SelectByKey"), Benchmark]
    public async Task<Shipper?> SelectByKey_Inquiry()
        => await _db.Shippers.SelectByKeyAsync(TargetShipperId);

    // ---- SelectByField (CompanyName) ----------------------------------------------------

    [BenchmarkCategory("SelectByField"), Benchmark(Baseline = true)]
    public async Task<int> SelectByField_AdoNet()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT ShipperID, CompanyName, Phone FROM Shippers WHERE CompanyName = $c;";
        command.Parameters.Add("$c", SqliteType.Text).Value = TargetCompanyName;
        var list = new List<Shipper>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) list.Add(ReadShipper(reader));
        return list.Count;
    }

    [BenchmarkCategory("SelectByField"), Benchmark]
    public async Task<int> SelectByField_Dapper()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        var list = (await connection.QueryAsync<Shipper>(
            "SELECT ShipperID, CompanyName, Phone FROM Shippers WHERE CompanyName = @c;",
            new { c = TargetCompanyName })).AsList();
        return list.Count;
    }

    [BenchmarkCategory("SelectByField"), Benchmark]
    public async Task<int> SelectByField_EfCore()
    {
        await using var ctx = await _db.DbContextFactory.CreateDbContextAsync();
        var list = await ctx.Shippers.AsNoTracking().Where(s => s.CompanyName == TargetCompanyName).ToListAsync();
        return list.Count;
    }

    [BenchmarkCategory("SelectByField"), Benchmark]
    public async Task<int> SelectByField_Inquiry()
    {
        var list = await _db.Shippers.SelectByCompanyNameAsync(TargetCompanyName);
        return list.Count;
    }

    // ---- Insert -------------------------------------------------------------------------

    [BenchmarkCategory("Insert"), Benchmark(Baseline = true)]
    public async Task<int> Insert_AdoNet()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO Shippers (CompanyName, Phone) VALUES ($company, $phone);";
        command.Parameters.Add("$company", SqliteType.Text).Value = "Bench Shipper";
        command.Parameters.Add("$phone",   SqliteType.Text).Value = "555-0000";
        return await command.ExecuteNonQueryAsync();
    }

    [BenchmarkCategory("Insert"), Benchmark]
    public async Task<int> Insert_Dapper()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        return await connection.ExecuteAsync(
            "INSERT INTO Shippers (CompanyName, Phone) VALUES (@company, @phone);",
            new { company = "Bench Shipper", phone = "555-0000" });
    }

    [BenchmarkCategory("Insert"), Benchmark]
    public async Task<int> Insert_EfCore()
    {
        await using var ctx = await _db.DbContextFactory.CreateDbContextAsync();
        ctx.Shippers.Add(new EfShipper { CompanyName = "Bench Shipper", Phone = "555-0000" });
        return await ctx.SaveChangesAsync();
    }

    [BenchmarkCategory("Insert"), Benchmark]
    public async Task<int> Insert_Inquiry()
        => await _db.Shippers.InsertAsync(new Shipper { CompanyName = "Bench Shipper", Phone = "555-0000" });

    // ---- Update -------------------------------------------------------------------------

    [BenchmarkCategory("Update"), Benchmark(Baseline = true)]
    public async Task<int> Update_AdoNet()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Shippers SET CompanyName = $company, Phone = $phone WHERE ShipperID = $id;";
        command.Parameters.Add("$id",      SqliteType.Integer).Value = TargetShipperId;
        command.Parameters.Add("$company", SqliteType.Text).Value    = "Updated Shipper";
        command.Parameters.Add("$phone",   SqliteType.Text).Value    = "555-9999";
        return await command.ExecuteNonQueryAsync();
    }

    [BenchmarkCategory("Update"), Benchmark]
    public async Task<int> Update_Dapper()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        return await connection.ExecuteAsync(
            "UPDATE Shippers SET CompanyName = @company, Phone = @phone WHERE ShipperID = @id;",
            new { id = TargetShipperId, company = "Updated Shipper", phone = "555-9999" });
    }

    [BenchmarkCategory("Update"), Benchmark]
    public async Task<int> Update_EfCore()
    {
        await using var ctx = await _db.DbContextFactory.CreateDbContextAsync();
        var entity = await ctx.Shippers.FirstAsync(s => s.ShipperID == TargetShipperId);
        entity.CompanyName = "Updated Shipper";
        entity.Phone       = "555-9999";
        return await ctx.SaveChangesAsync();
    }

    [BenchmarkCategory("Update"), Benchmark]
    public async Task<bool> Update_Inquiry()
        => await _db.Shippers.UpdateAsync(new Shipper
        {
            ShipperID   = TargetShipperId,
            CompanyName = "Updated Shipper",
            Phone       = "555-9999",
        });

    // ---- Upsert -------------------------------------------------------------------------

    [BenchmarkCategory("Upsert"), Benchmark(Baseline = true)]
    public async Task<int> Upsert_AdoNet()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO Shippers (ShipperID, CompanyName, Phone) VALUES ($id, $company, $phone) " +
            "ON CONFLICT(ShipperID) DO UPDATE SET " +
            "    CompanyName = excluded.CompanyName, " +
            "    Phone       = excluded.Phone;";
        command.Parameters.Add("$id",      SqliteType.Integer).Value = TargetShipperId;
        command.Parameters.Add("$company", SqliteType.Text).Value    = "Upserted Shipper";
        command.Parameters.Add("$phone",   SqliteType.Text).Value    = "555-1234";
        return await command.ExecuteNonQueryAsync();
    }

    [BenchmarkCategory("Upsert"), Benchmark]
    public async Task<int> Upsert_Dapper()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        return await connection.ExecuteAsync(
            "INSERT INTO Shippers (ShipperID, CompanyName, Phone) VALUES (@id, @company, @phone) " +
            "ON CONFLICT(ShipperID) DO UPDATE SET " +
            "    CompanyName = excluded.CompanyName, " +
            "    Phone       = excluded.Phone;",
            new { id = TargetShipperId, company = "Upserted Shipper", phone = "555-1234" });
    }

    [BenchmarkCategory("Upsert"), Benchmark]
    public async Task<int> Upsert_EfCore()
    {
        await using var ctx = await _db.DbContextFactory.CreateDbContextAsync();
        return await ctx.Database.ExecuteSqlRawAsync(
            "INSERT INTO Shippers (ShipperID, CompanyName, Phone) VALUES ({0}, {1}, {2}) " +
            "ON CONFLICT(ShipperID) DO UPDATE SET " +
            "    CompanyName = excluded.CompanyName, " +
            "    Phone       = excluded.Phone;",
            TargetShipperId, "Upserted Shipper", "555-1234");
    }

    [BenchmarkCategory("Upsert"), Benchmark]
    public async Task<int> Upsert_Inquiry()
        => await _db.Shippers.UpsertAsync(new Shipper
        {
            ShipperID   = TargetShipperId,
            CompanyName = "Upserted Shipper",
            Phone       = "555-1234",
        });
}
