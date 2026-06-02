using System.Data;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Dapper;
using Inquiry.Benchmarks.PostgreSql.Ef;
using Inquiry.Northwind.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Inquiry.Benchmarks.PostgreSql;

/// <summary>
/// CRUD comparison for the Northwind <c>Shippers</c> table against PostgreSQL, using
/// Inquiry's GENERATED (compile-time) store — four legs per operation: raw ADO.NET
/// (baseline), Dapper, EF Core, and the generated Inquiry store.
/// </summary>
/// <remarks>
/// The database is provisioned once per <see cref="Rows"/> parameter tier via
/// <see cref="PostgreSqlBenchmarkDatabase"/> (Testcontainer, <c>postgres:16-alpine</c>).
/// Requires Docker.
/// </remarks>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class ShipperBenchmarks
{
    private PostgreSqlBenchmarkDatabase _db = null!;

    // Single fixed target: ShipperID = 1 (always present after seeding).
    private const int    TargetShipperId   = 1;
    private const string TargetCompanyName = "Shipper 0";

    [Params(1000)]
    public int Rows;

    private const string SelectAllSql     = "SELECT \"ShipperID\", \"CompanyName\", \"Phone\" FROM \"Shippers\"";
    private const string SelectByKeySql   = SelectAllSql + " WHERE \"ShipperID\" = @id";
    private const string SelectByFieldSql = SelectAllSql + " WHERE \"CompanyName\" = @c";

    [GlobalSetup]
    public void GlobalSetup()
        => _db = PostgreSqlBenchmarkDatabase.CreateAsync(Rows).GetAwaiter().GetResult();

    [GlobalCleanup]
    public void GlobalCleanup()
        => _db.DisposeAsync().AsTask().GetAwaiter().GetResult();

    private static Shipper ReadShipper(System.Data.Common.DbDataReader reader) => new Shipper
    {
        ShipperID   = reader.GetInt32(0),
        CompanyName = reader.GetString(1),
        Phone       = reader.IsDBNull(2) ? null : reader.GetString(2),
    };

    private NpgsqlConnection OpenConnection() => new NpgsqlConnection(_db.ConnectionString);

    // ---- SelectAll ----------------------------------------------------------------------

    [BenchmarkCategory("SelectAll"), Benchmark(Baseline = true)]
    public async Task<int> SelectAll_AdoNet()
    {
        await using var connection = OpenConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = SelectAllSql;
        var list = new List<Shipper>(_db.RowCount);
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleResult);
        while (await reader.ReadAsync()) list.Add(ReadShipper(reader));
        return list.Count;
    }

    [BenchmarkCategory("SelectAll"), Benchmark]
    public async Task<int> SelectAll_Dapper()
    {
        await using var connection = OpenConnection();
        await connection.OpenAsync();
        return (await connection.QueryAsync<Shipper>(SelectAllSql)).AsList().Count;
    }

    [BenchmarkCategory("SelectAll"), Benchmark]
    public async Task<int> SelectAll_EfCore()
    {
        await using var ctx = await _db.DbContextFactory.CreateDbContextAsync();
        return (await ctx.Shippers.AsNoTracking().ToListAsync()).Count;
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
        await using var connection = OpenConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = SelectByKeySql;
        command.Parameters.AddWithValue("id", TargetShipperId);
        // Fair floor: SingleRow|SingleResult — the same CommandBehavior Inquiry's pipeline and Dapper request for a point read.
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleResult | CommandBehavior.SingleRow);
        return await reader.ReadAsync() ? ReadShipper(reader) : null;
    }

    [BenchmarkCategory("SelectByKey"), Benchmark]
    public async Task<Shipper?> SelectByKey_Dapper()
    {
        await using var connection = OpenConnection();
        await connection.OpenAsync();
        return await connection.QuerySingleOrDefaultAsync<Shipper>(SelectByKeySql, new { id = TargetShipperId });
    }

    [BenchmarkCategory("SelectByKey"), Benchmark]
    public async Task<PgEfShipper?> SelectByKey_EfCore()
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
        await using var connection = OpenConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = SelectByFieldSql;
        command.Parameters.AddWithValue("c", TargetCompanyName);
        var list = new List<Shipper>();
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleResult);
        while (await reader.ReadAsync()) list.Add(ReadShipper(reader));
        return list.Count;
    }

    [BenchmarkCategory("SelectByField"), Benchmark]
    public async Task<int> SelectByField_Dapper()
    {
        await using var connection = OpenConnection();
        await connection.OpenAsync();
        var list = (await connection.QueryAsync<Shipper>(SelectByFieldSql, new { c = TargetCompanyName })).AsList();
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
        await using var connection = OpenConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO \"Shippers\" (\"CompanyName\", \"Phone\") VALUES (@company, @phone);";
        command.Parameters.AddWithValue("company", "Bench Shipper");
        command.Parameters.AddWithValue("phone",   "555-0000");
        return await command.ExecuteNonQueryAsync();
    }

    [BenchmarkCategory("Insert"), Benchmark]
    public async Task<int> Insert_Dapper()
    {
        await using var connection = OpenConnection();
        await connection.OpenAsync();
        return await connection.ExecuteAsync(
            "INSERT INTO \"Shippers\" (\"CompanyName\", \"Phone\") VALUES (@company, @phone);",
            new { company = "Bench Shipper", phone = "555-0000" });
    }

    [BenchmarkCategory("Insert"), Benchmark]
    public async Task<int> Insert_EfCore()
    {
        await using var ctx = await _db.DbContextFactory.CreateDbContextAsync();
        ctx.Shippers.Add(new PgEfShipper { CompanyName = "Bench Shipper", Phone = "555-0000" });
        return await ctx.SaveChangesAsync();
    }

    [BenchmarkCategory("Insert"), Benchmark]
    public async Task<int> Insert_Inquiry()
        => await _db.Shippers.InsertAsync(new Shipper { CompanyName = "Bench Shipper", Phone = "555-0000" });

    // ---- Update -------------------------------------------------------------------------

    [BenchmarkCategory("Update"), Benchmark(Baseline = true)]
    public async Task<int> Update_AdoNet()
    {
        await using var connection = OpenConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE \"Shippers\" SET \"CompanyName\" = @company, \"Phone\" = @phone WHERE \"ShipperID\" = @id;";
        command.Parameters.AddWithValue("id",      TargetShipperId);
        command.Parameters.AddWithValue("company", "Updated Shipper");
        command.Parameters.AddWithValue("phone",   "555-9999");
        return await command.ExecuteNonQueryAsync();
    }

    [BenchmarkCategory("Update"), Benchmark]
    public async Task<int> Update_Dapper()
    {
        await using var connection = OpenConnection();
        await connection.OpenAsync();
        return await connection.ExecuteAsync(
            "UPDATE \"Shippers\" SET \"CompanyName\" = @company, \"Phone\" = @phone WHERE \"ShipperID\" = @id;",
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
        await using var connection = OpenConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO \"Shippers\" (\"ShipperID\", \"CompanyName\", \"Phone\") VALUES (@id, @company, @phone) " +
            "ON CONFLICT(\"ShipperID\") DO UPDATE SET " +
            "    \"CompanyName\" = EXCLUDED.\"CompanyName\", " +
            "    \"Phone\"       = EXCLUDED.\"Phone\";";
        command.Parameters.AddWithValue("id",      TargetShipperId);
        command.Parameters.AddWithValue("company", "Upserted Shipper");
        command.Parameters.AddWithValue("phone",   "555-1234");
        return await command.ExecuteNonQueryAsync();
    }

    [BenchmarkCategory("Upsert"), Benchmark]
    public async Task<int> Upsert_Dapper()
    {
        await using var connection = OpenConnection();
        await connection.OpenAsync();
        return await connection.ExecuteAsync(
            "INSERT INTO \"Shippers\" (\"ShipperID\", \"CompanyName\", \"Phone\") VALUES (@id, @company, @phone) " +
            "ON CONFLICT(\"ShipperID\") DO UPDATE SET " +
            "    \"CompanyName\" = EXCLUDED.\"CompanyName\", " +
            "    \"Phone\"       = EXCLUDED.\"Phone\";",
            new { id = TargetShipperId, company = "Upserted Shipper", phone = "555-1234" });
    }

    [BenchmarkCategory("Upsert"), Benchmark]
    public async Task<int> Upsert_EfCore()
    {
        await using var ctx = await _db.DbContextFactory.CreateDbContextAsync();
        return await ctx.Database.ExecuteSqlRawAsync(
            "INSERT INTO \"Shippers\" (\"ShipperID\", \"CompanyName\", \"Phone\") VALUES ({0}, {1}, {2}) " +
            "ON CONFLICT(\"ShipperID\") DO UPDATE SET " +
            "    \"CompanyName\" = EXCLUDED.\"CompanyName\", " +
            "    \"Phone\"       = EXCLUDED.\"Phone\";",
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
