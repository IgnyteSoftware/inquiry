using System.Data;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Order;
using Dapper;
using Inquiry.Benchmarks.SqlServer.Ef;
using Inquiry.Northwind.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Inquiry.Benchmarks.SqlServer;

/// <summary>
/// CRUD comparison for the Northwind <c>Shippers</c> table against SQL Server, using
/// Inquiry's GENERATED (compile-time) store — four legs per operation: raw ADO.NET
/// (baseline), Dapper, EF Core, and the generated Inquiry store.
/// </summary>
/// <remarks>
/// The database is provisioned once per <see cref="Rows"/> parameter tier via
/// <see cref="SqlServerBenchmarkDatabase"/> (Testcontainer, <c>mcr.microsoft.com/mssql/server:2022-latest</c>).
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
    private SqlServerBenchmarkDatabase _db = null!;

    // Single fixed target: ShipperID = 1 (always present after seeding).
    private const int    TargetShipperId   = 1;
    private const string TargetCompanyName = "Shipper 0";

    [Params(1000)]
    public int Rows;

    private const string SelectAllSql     = "SELECT [ShipperID], [CompanyName], [Phone] FROM [Shippers]";
    private const string SelectByKeySql   = SelectAllSql + " WHERE [ShipperID] = @id";
    private const string SelectByFieldSql = SelectAllSql + " WHERE [CompanyName] = @c";

    [GlobalSetup]
    public void GlobalSetup()
        => _db = SqlServerBenchmarkDatabase.CreateAsync(Rows).GetAwaiter().GetResult();

    [GlobalCleanup]
    public void GlobalCleanup()
        => _db.DisposeAsync().AsTask().GetAwaiter().GetResult();

    private static Shipper ReadShipper(System.Data.Common.DbDataReader reader) => new Shipper
    {
        ShipperID   = reader.GetInt32(0),
        CompanyName = reader.GetString(1),
        Phone       = reader.IsDBNull(2) ? null : reader.GetString(2),
    };

    private SqlConnection OpenConnection() => new SqlConnection(_db.ConnectionString);

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
        command.Parameters.AddWithValue("@id", TargetShipperId);
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
    public async Task<SqlServerEfShipper?> SelectByKey_EfCore()
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
        command.Parameters.AddWithValue("@c", TargetCompanyName);
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
        // ShipperID is IDENTITY — do not supply it.
        command.CommandText = "INSERT INTO [Shippers] ([CompanyName], [Phone]) VALUES (@company, @phone);";
        command.Parameters.AddWithValue("@company", "Bench Shipper");
        command.Parameters.AddWithValue("@phone",   "555-0000");
        return await command.ExecuteNonQueryAsync();
    }

    [BenchmarkCategory("Insert"), Benchmark]
    public async Task<int> Insert_Dapper()
    {
        await using var connection = OpenConnection();
        await connection.OpenAsync();
        return await connection.ExecuteAsync(
            "INSERT INTO [Shippers] ([CompanyName], [Phone]) VALUES (@company, @phone);",
            new { company = "Bench Shipper", phone = "555-0000" });
    }

    [BenchmarkCategory("Insert"), Benchmark]
    public async Task<int> Insert_EfCore()
    {
        await using var ctx = await _db.DbContextFactory.CreateDbContextAsync();
        ctx.Shippers.Add(new SqlServerEfShipper { CompanyName = "Bench Shipper", Phone = "555-0000" });
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
        command.CommandText = "UPDATE [Shippers] SET [CompanyName] = @company, [Phone] = @phone WHERE [ShipperID] = @id;";
        command.Parameters.AddWithValue("@id",      TargetShipperId);
        command.Parameters.AddWithValue("@company", "Updated Shipper");
        command.Parameters.AddWithValue("@phone",   "555-9999");
        return await command.ExecuteNonQueryAsync();
    }

    [BenchmarkCategory("Update"), Benchmark]
    public async Task<int> Update_Dapper()
    {
        await using var connection = OpenConnection();
        await connection.OpenAsync();
        return await connection.ExecuteAsync(
            "UPDATE [Shippers] SET [CompanyName] = @company, [Phone] = @phone WHERE [ShipperID] = @id;",
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
    // SQL Server has no ON CONFLICT; use MERGE INTO ... WHEN MATCHED / WHEN NOT MATCHED.
    // HOLDLOCK prevents a race between the existence check and the insert in concurrent scenarios.

    [BenchmarkCategory("Upsert"), Benchmark(Baseline = true)]
    public async Task<int> Upsert_AdoNet()
    {
        await using var connection = OpenConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "MERGE INTO [Shippers] WITH (HOLDLOCK) AS target " +
            "USING (SELECT @id AS [ShipperID], @company AS [CompanyName], @phone AS [Phone]) AS source " +
            "    ON target.[ShipperID] = source.[ShipperID] " +
            "WHEN MATCHED THEN " +
            "    UPDATE SET target.[CompanyName] = source.[CompanyName], target.[Phone] = source.[Phone] " +
            "WHEN NOT MATCHED THEN " +
            "    INSERT ([ShipperID], [CompanyName], [Phone]) " +
            "    VALUES (source.[ShipperID], source.[CompanyName], source.[Phone]);";
        command.Parameters.AddWithValue("@id",      TargetShipperId);
        command.Parameters.AddWithValue("@company", "Upserted Shipper");
        command.Parameters.AddWithValue("@phone",   "555-1234");
        return await command.ExecuteNonQueryAsync();
    }

    [BenchmarkCategory("Upsert"), Benchmark]
    public async Task<int> Upsert_Dapper()
    {
        await using var connection = OpenConnection();
        await connection.OpenAsync();
        return await connection.ExecuteAsync(
            "MERGE INTO [Shippers] WITH (HOLDLOCK) AS target " +
            "USING (SELECT @id AS [ShipperID], @company AS [CompanyName], @phone AS [Phone]) AS source " +
            "    ON target.[ShipperID] = source.[ShipperID] " +
            "WHEN MATCHED THEN " +
            "    UPDATE SET target.[CompanyName] = source.[CompanyName], target.[Phone] = source.[Phone] " +
            "WHEN NOT MATCHED THEN " +
            "    INSERT ([ShipperID], [CompanyName], [Phone]) " +
            "    VALUES (source.[ShipperID], source.[CompanyName], source.[Phone]);",
            new { id = TargetShipperId, company = "Upserted Shipper", phone = "555-1234" });
    }

    [BenchmarkCategory("Upsert"), Benchmark]
    public async Task<int> Upsert_EfCore()
    {
        await using var ctx = await _db.DbContextFactory.CreateDbContextAsync();
        return await ctx.Database.ExecuteSqlRawAsync(
            "MERGE INTO [Shippers] WITH (HOLDLOCK) AS target " +
            "USING (SELECT {0} AS [ShipperID], {1} AS [CompanyName], {2} AS [Phone]) AS source " +
            "    ON target.[ShipperID] = source.[ShipperID] " +
            "WHEN MATCHED THEN " +
            "    UPDATE SET target.[CompanyName] = source.[CompanyName], target.[Phone] = source.[Phone] " +
            "WHEN NOT MATCHED THEN " +
            "    INSERT ([ShipperID], [CompanyName], [Phone]) " +
            "    VALUES (source.[ShipperID], source.[CompanyName], source.[Phone]);",
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
