using System.Data;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Order;
using Dapper;
using Inquiry.Benchmarks.Oracle.Ef;
using Inquiry.Northwind.Models;
using Microsoft.EntityFrameworkCore;
using Oracle.ManagedDataAccess.Client;

namespace Inquiry.Benchmarks.Oracle;

/// <summary>
/// CRUD comparison for the Northwind <c>Shippers</c> table against Oracle, using
/// Inquiry's GENERATED (compile-time) store — four legs per operation: raw ADO.NET
/// (baseline), Dapper, EF Core, and the generated Inquiry store.
/// </summary>
/// <remarks>
/// The database is provisioned once per <see cref="Rows"/> parameter tier via
/// <see cref="OracleBenchmarkDatabase"/> (Testcontainer, <c>gvenzl/oracle-xe:21-slim-faststart</c>).
/// Requires Docker.
/// </remarks>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
// Run in declared order so the (non-mutating) read benchmarks all execute before Insert grows the
// shared table; Update/Upsert target a stable key and run last. Lets the container be shared.
[Orderer(SummaryOrderPolicy.Declared, MethodOrderPolicy.Declared)]
public class ShipperBenchmarks
{
    private OracleBenchmarkDatabase _db = null!;

    // Single fixed target: ShipperID = 1 (always present after seeding).
    private const int    TargetShipperId   = 1;
    private const string TargetCompanyName = "Shipper 0";

    [Params(1000)]
    public int Rows;

    // Oracle DDL uses unquoted identifiers — Oracle folds them to uppercase.
    // Hand-written SQL uses the stored uppercase names to stay consistent.
    private const string SelectAllSql     = "SELECT SHIPPERID, COMPANYNAME, PHONE FROM SHIPPERS";
    private const string SelectByKeySql   = SelectAllSql + " WHERE SHIPPERID = :id";
    private const string SelectByFieldSql = SelectAllSql + " WHERE COMPANYNAME = :c";

    [GlobalSetup]
    public void GlobalSetup()
        => _db = OracleBenchmarkDatabase.CreateAsync(Rows).GetAwaiter().GetResult();

    [GlobalCleanup]
    public void GlobalCleanup()
        => _db.DisposeAsync().AsTask().GetAwaiter().GetResult();

    private static Shipper ReadShipper(System.Data.Common.DbDataReader reader) => new Shipper
    {
        ShipperID   = reader.GetInt32(0),
        CompanyName = reader.GetString(1),
        Phone       = reader.IsDBNull(2) ? null : reader.GetString(2),
    };

    private OracleConnection OpenConnection()
    {
        var conn = new OracleConnection(_db.ConnectionString);
        return conn;
    }

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
        command.BindByName = true;
        command.CommandText = SelectByKeySql;
        command.Parameters.Add("id", OracleDbType.Int32).Value = TargetShipperId;
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
    public async Task<OracleEfShipper?> SelectByKey_EfCore()
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
        command.BindByName = true;
        command.CommandText = SelectByFieldSql;
        command.Parameters.Add("c", OracleDbType.Varchar2).Value = TargetCompanyName;
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
        command.BindByName = true;
        command.CommandText = "INSERT INTO SHIPPERS (COMPANYNAME, PHONE) VALUES (:company, :phone)";
        command.Parameters.Add("company", OracleDbType.Varchar2).Value = "Bench Shipper";
        command.Parameters.Add("phone",   OracleDbType.Varchar2).Value = "555-0000";
        return await command.ExecuteNonQueryAsync();
    }

    [BenchmarkCategory("Insert"), Benchmark]
    public async Task<int> Insert_Dapper()
    {
        await using var connection = OpenConnection();
        await connection.OpenAsync();
        return await connection.ExecuteAsync(
            "INSERT INTO SHIPPERS (COMPANYNAME, PHONE) VALUES (:company, :phone)",
            new { company = "Bench Shipper", phone = "555-0000" });
    }

    [BenchmarkCategory("Insert"), Benchmark]
    public async Task<int> Insert_EfCore()
    {
        await using var ctx = await _db.DbContextFactory.CreateDbContextAsync();
        ctx.Shippers.Add(new OracleEfShipper { CompanyName = "Bench Shipper", Phone = "555-0000" });
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
        command.BindByName = true;
        command.CommandText = "UPDATE SHIPPERS SET COMPANYNAME = :company, PHONE = :phone WHERE SHIPPERID = :id";
        command.Parameters.Add("id",      OracleDbType.Int32).Value   = TargetShipperId;
        command.Parameters.Add("company", OracleDbType.Varchar2).Value = "Updated Shipper";
        command.Parameters.Add("phone",   OracleDbType.Varchar2).Value = "555-9999";
        return await command.ExecuteNonQueryAsync();
    }

    [BenchmarkCategory("Update"), Benchmark]
    public async Task<int> Update_Dapper()
    {
        await using var connection = OpenConnection();
        await connection.OpenAsync();
        return await connection.ExecuteAsync(
            "UPDATE SHIPPERS SET COMPANYNAME = :company, PHONE = :phone WHERE SHIPPERID = :id",
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
    // Oracle has no INSERT ... ON CONFLICT; use MERGE INTO.

    [BenchmarkCategory("Upsert"), Benchmark(Baseline = true)]
    public async Task<int> Upsert_AdoNet()
    {
        await using var connection = OpenConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.BindByName = true;
        command.CommandText =
            "MERGE INTO SHIPPERS tgt " +
            "USING (SELECT :id AS SHIPPERID, :company AS COMPANYNAME, :phone AS PHONE FROM dual) src " +
            "ON (tgt.SHIPPERID = src.SHIPPERID) " +
            "WHEN MATCHED THEN UPDATE SET tgt.COMPANYNAME = src.COMPANYNAME, tgt.PHONE = src.PHONE " +
            "WHEN NOT MATCHED THEN INSERT (COMPANYNAME, PHONE) VALUES (src.COMPANYNAME, src.PHONE)";
        command.Parameters.Add("id",      OracleDbType.Int32).Value   = TargetShipperId;
        command.Parameters.Add("company", OracleDbType.Varchar2).Value = "Upserted Shipper";
        command.Parameters.Add("phone",   OracleDbType.Varchar2).Value = "555-1234";
        return await command.ExecuteNonQueryAsync();
    }

    [BenchmarkCategory("Upsert"), Benchmark]
    public async Task<int> Upsert_Dapper()
    {
        await using var connection = OpenConnection();
        await connection.OpenAsync();
        return await connection.ExecuteAsync(
            "MERGE INTO SHIPPERS tgt " +
            "USING (SELECT :id AS SHIPPERID, :company AS COMPANYNAME, :phone AS PHONE FROM dual) src " +
            "ON (tgt.SHIPPERID = src.SHIPPERID) " +
            "WHEN MATCHED THEN UPDATE SET tgt.COMPANYNAME = src.COMPANYNAME, tgt.PHONE = src.PHONE " +
            "WHEN NOT MATCHED THEN INSERT (COMPANYNAME, PHONE) VALUES (src.COMPANYNAME, src.PHONE)",
            new { id = TargetShipperId, company = "Upserted Shipper", phone = "555-1234" });
    }

    [BenchmarkCategory("Upsert"), Benchmark]
    public async Task<int> Upsert_EfCore()
    {
        await using var ctx = await _db.DbContextFactory.CreateDbContextAsync();
        return await ctx.Database.ExecuteSqlRawAsync(
            "MERGE INTO SHIPPERS tgt " +
            "USING (SELECT {0} AS SHIPPERID, {1} AS COMPANYNAME, {2} AS PHONE FROM dual) src " +
            "ON (tgt.SHIPPERID = src.SHIPPERID) " +
            "WHEN MATCHED THEN UPDATE SET tgt.COMPANYNAME = src.COMPANYNAME, tgt.PHONE = src.PHONE " +
            "WHEN NOT MATCHED THEN INSERT (COMPANYNAME, PHONE) VALUES (src.COMPANYNAME, src.PHONE)",
            TargetShipperId, "Upserted Shipper", "555-1234");
    }

    // note: Inquiry's Oracle dialect emits an INQ039 NotSupportedException stub for UpsertAsync
    // (IDENTITY-key upsert is unsupported on Oracle), so the Inquiry Upsert leg is omitted here --
    // it would throw at runtime. The ADO/Dapper/EF MERGE upserts above remain a valid 3-way
    // comparison. (Inquiry covers Upsert on the other four dialects.)
}
