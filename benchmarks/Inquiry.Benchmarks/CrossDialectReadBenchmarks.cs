using System.Data;
using System.Data.Common;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Dapper;
using Inquiry.Benchmarks.Ef;
using Inquiry.DependencyInjection;
using Inquiry.MySql.DependencyInjection;
using Inquiry.Northwind.Models;
using Inquiry.PostgreSql.DependencyInjection;
using Inquiry.SqlServer.DependencyInjection;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using Npgsql;
using Testcontainers.MsSql;
using Testcontainers.MySql;
using Testcontainers.PostgreSql;

namespace Inquiry.Benchmarks;

/// <summary>
/// Cross-dialect read comparison of all four libraries — raw ADO.NET (baseline), Dapper, EF Core, and
/// Inquiry — against the networked engines (PostgreSQL, MySQL, SQL Server) provisioned via Testcontainers.
/// Read hot-paths (SelectAll + SelectByKey) on the minimal <c>shippers</c> table (3 columns).
/// </summary>
/// <remarks>
/// Identifiers are all-lowercase so a single physical table is addressable identically by EF Core (quotes
/// identifiers), the others (portable unquoted SQL), and each engine's folding/casing rules — this is what
/// lets EF Core join the cross-dialect comparison. Inquiry runs through its ad-hoc <c>IInquiry.Query…</c>
/// path so all dialects share one assembly (its generated store fast-path is compile-time-per-dialect); on a
/// networked engine the round-trip dominates, so the few-µs ad-hoc-vs-generated difference (visible only in
/// the in-process SQLite suite) is negligible. ADO.NET is the <c>[Baseline]</c>, so the Ratio / Alloc Ratio
/// columns read as each library's overhead over hand-written ADO.NET. Requires Docker.
/// </remarks>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class CrossDialectReadBenchmarks
{
    private const int SeedRows = 1000;
    private const int TargetShipperId = 1;
    private const string SelectAllSql = "SELECT shipperid, companyname, phone FROM shippers";
    private const string SelectByKeySql = SelectAllSql + " WHERE shipperid = @id";

    [Params("PostgreSql", "MySql", "SqlServer")]
    public string Dialect { get; set; } = null!;

    private IAsyncDisposable _container = null!;
    private string _connectionString = null!;
    private ServiceProvider _services = null!;
    private IInquiry _inquiry = null!;
    private Func<DbConnection> _open = null!;
    private DbContextOptions<CrossDialectShipperContext> _efOptions = null!;

    [GlobalSetup]
    public void Setup() => SetupAsync().GetAwaiter().GetResult();

    private async Task SetupAsync()
    {
        // Force-load the Northwind assembly before AddInquiry() so its generated IInquiryServiceRegistration
        // (which registers the entity materializers used by the ad-hoc query path) is found by reflection.
        _ = typeof(Shipper).Assembly;

        string ddl;
        IServiceCollection services = new ServiceCollection().AddInquiry();
        var efBuilder = new DbContextOptionsBuilder<CrossDialectShipperContext>();

        switch (Dialect)
        {
            case "PostgreSql":
            {
                var c = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
                await c.StartAsync();
                _container = c;
                _connectionString = c.GetConnectionString();
                ddl = "CREATE TABLE shippers (shipperid SERIAL PRIMARY KEY, companyname VARCHAR(40) NOT NULL, phone VARCHAR(40));";
                _open = () => new NpgsqlConnection(_connectionString);
                services.AddInquiryPostgreSql(_connectionString);
                efBuilder.UseNpgsql(_connectionString);
                break;
            }

            case "MySql":
            {
                var c = new MySqlBuilder().WithImage("mysql:8.0").Build();
                await c.StartAsync();
                _container = c;
                _connectionString = c.GetConnectionString();
                ddl = "CREATE TABLE shippers (shipperid INT AUTO_INCREMENT PRIMARY KEY, companyname VARCHAR(40) NOT NULL, phone VARCHAR(40));";
                _open = () => new MySqlConnection(_connectionString);
                services.AddInquiryMySql(_connectionString);
                efBuilder.UseMySql(_connectionString, new MySqlServerVersion(new Version(8, 0, 0)));
                break;
            }

            case "SqlServer":
            {
                var c = new MsSqlBuilder().WithImage("mcr.microsoft.com/mssql/server:2022-latest").Build();
                await c.StartAsync();
                _container = c;
                _connectionString = c.GetConnectionString();
                ddl = "CREATE TABLE shippers (shipperid INT IDENTITY PRIMARY KEY, companyname NVARCHAR(40) NOT NULL, phone NVARCHAR(40));";
                _open = () => new SqlConnection(_connectionString);
                services.AddInquirySqlServer(_connectionString);
                efBuilder.UseSqlServer(_connectionString);
                break;
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(Dialect), Dialect, "Unsupported dialect.");
        }

        _services = services.BuildServiceProvider();
        _inquiry = _services.GetRequiredService<IInquiry>();
        _efOptions = efBuilder.Options;

        await using var connection = _open();
        await connection.OpenAsync();
        await using (var create = connection.CreateCommand()) { create.CommandText = ddl; await create.ExecuteNonQueryAsync(); }
        await using (var insert = connection.CreateCommand())
        {
            insert.CommandText = "INSERT INTO shippers (companyname, phone) VALUES (@c, @p)";
            var pc = insert.CreateParameter(); pc.ParameterName = "@c"; insert.Parameters.Add(pc);
            var pp = insert.CreateParameter(); pp.ParameterName = "@p"; insert.Parameters.Add(pp);
            for (var i = 0; i < SeedRows; i++)
            {
                pc.Value = $"Shipper {i}";
                pp.Value = $"555-{i:0000}";
                await insert.ExecuteNonQueryAsync();
            }
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _services?.Dispose();
        _container?.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private static Shipper ReadShipper(DbDataReader reader) => new Shipper
    {
        ShipperID   = reader.GetInt32(0),
        CompanyName = reader.GetString(1),
        Phone       = reader.IsDBNull(2) ? null : reader.GetString(2),
    };

    // ---- SelectAll ----------------------------------------------------------------------

    [BenchmarkCategory("SelectAll"), Benchmark(Baseline = true)]
    public async Task<int> SelectAll_AdoNet()
    {
        await using var connection = _open();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = SelectAllSql;
        var list = new List<Shipper>(SeedRows);
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleResult | CommandBehavior.SequentialAccess);
        while (await reader.ReadAsync()) list.Add(ReadShipper(reader));
        return list.Count;
    }

    [BenchmarkCategory("SelectAll"), Benchmark]
    public async Task<int> SelectAll_Dapper()
    {
        await using var connection = _open();
        await connection.OpenAsync();
        return (await connection.QueryAsync<Shipper>(SelectAllSql)).AsList().Count;
    }

    [BenchmarkCategory("SelectAll"), Benchmark]
    public async Task<int> SelectAll_EfCore()
    {
        await using var ctx = new CrossDialectShipperContext(_efOptions);
        return (await ctx.Shippers.AsNoTracking().ToListAsync()).Count;
    }

    [BenchmarkCategory("SelectAll"), Benchmark]
    public async Task<int> SelectAll_Inquiry()
        => (await _inquiry.QueryListAsync<Shipper>(SelectAllSql)).Count;

    // ---- SelectByKey --------------------------------------------------------------------

    [BenchmarkCategory("SelectByKey"), Benchmark(Baseline = true)]
    public async Task<Shipper?> SelectByKey_AdoNet()
    {
        await using var connection = _open();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = SelectByKeySql;
        var p = command.CreateParameter(); p.ParameterName = "@id"; p.Value = TargetShipperId; command.Parameters.Add(p);
        // Fair floor: SingleRow|SingleResult — the same CommandBehavior Inquiry's pipeline and Dapper request for a point read.
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleResult | CommandBehavior.SingleRow | CommandBehavior.SequentialAccess);
        return await reader.ReadAsync() ? ReadShipper(reader) : null;
    }

    [BenchmarkCategory("SelectByKey"), Benchmark]
    public async Task<Shipper?> SelectByKey_Dapper()
    {
        await using var connection = _open();
        await connection.OpenAsync();
        return await connection.QuerySingleOrDefaultAsync<Shipper>(SelectByKeySql, new { id = TargetShipperId });
    }

    [BenchmarkCategory("SelectByKey"), Benchmark]
    public async Task<EfShipper?> SelectByKey_EfCore()
    {
        await using var ctx = new CrossDialectShipperContext(_efOptions);
        return await ctx.Shippers.AsNoTracking().FirstOrDefaultAsync(s => s.ShipperID == TargetShipperId);
    }

    [BenchmarkCategory("SelectByKey"), Benchmark]
    public async Task<Shipper?> SelectByKey_Inquiry()
        => await _inquiry.QuerySingleOrDefaultAsync<Shipper>(SelectByKeySql, new { id = TargetShipperId });
}
