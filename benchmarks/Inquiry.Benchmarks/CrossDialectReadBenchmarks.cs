using System.Data.Common;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Dapper;
using Inquiry.DependencyInjection;
using Inquiry.MySql.DependencyInjection;
using Inquiry.Northwind.Models;
using Inquiry.PostgreSql.DependencyInjection;
using Inquiry.SqlServer.DependencyInjection;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using Npgsql;
using Testcontainers.MsSql;
using Testcontainers.MySql;
using Testcontainers.PostgreSql;

namespace Inquiry.Benchmarks;

/// <summary>
/// Cross-dialect read comparison of Inquiry vs Dapper against the networked engines (PostgreSQL, MySQL,
/// SQL Server) provisioned via Testcontainers. Read hot-paths (SelectAll + SelectByKey) on the minimal
/// <c>Shippers</c> table.
/// </summary>
/// <remarks>
/// Inquiry is exercised through its ad-hoc <c>IInquiry.Query…</c> path so all dialects share one assembly
/// (the generated store fast-path is compile-time-per-dialect). On a networked engine the round-trip
/// dominates, so the ad-hoc-vs-generated overhead difference (a few µs, visible only in the in-process
/// SQLite suite) is negligible — making this a fair library comparison on real databases. EF Core is
/// compared in the in-process SQLite suite (the definitive library-overhead measurement); it is omitted
/// here because its quoted-identifier convention conflicts with the portable unquoted SQL on PostgreSQL.
/// Requires Docker.
/// </remarks>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class CrossDialectReadBenchmarks
{
    private const int SeedRows = 1000;
    private const int TargetShipperId = 1;
    private const string SelectAllSql = "SELECT ShipperID, CompanyName, Phone FROM Shippers";
    private const string SelectByKeySql = SelectAllSql + " WHERE ShipperID = @id";

    [Params("PostgreSql", "MySql", "SqlServer")]
    public string Dialect { get; set; } = null!;

    private IAsyncDisposable _container = null!;
    private string _connectionString = null!;
    private ServiceProvider _services = null!;
    private IInquiry _inquiry = null!;
    private Func<DbConnection> _open = null!;

    [GlobalSetup]
    public void Setup() => SetupAsync().GetAwaiter().GetResult();

    private async Task SetupAsync()
    {
        // Force-load the Northwind assembly before AddInquiry() so its generated IInquiryServiceRegistration
        // (which registers the entity materializers used by the ad-hoc query path) is found by reflection.
        _ = typeof(Shipper).Assembly;

        string ddl;
        IServiceCollection services = new ServiceCollection().AddInquiry();

        switch (Dialect)
        {
            case "PostgreSql":
            {
                var c = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
                await c.StartAsync();
                _container = c;
                _connectionString = c.GetConnectionString();
                ddl = "CREATE TABLE Shippers (ShipperID SERIAL PRIMARY KEY, CompanyName VARCHAR(40) NOT NULL, Phone VARCHAR(40));";
                _open = () => new NpgsqlConnection(_connectionString);
                services.AddInquiryPostgreSql(_connectionString);
                break;
            }

            case "MySql":
            {
                var c = new MySqlBuilder().WithImage("mysql:8.0").Build();
                await c.StartAsync();
                _container = c;
                _connectionString = c.GetConnectionString();
                ddl = "CREATE TABLE Shippers (ShipperID INT AUTO_INCREMENT PRIMARY KEY, CompanyName VARCHAR(40) NOT NULL, Phone VARCHAR(40));";
                _open = () => new MySqlConnection(_connectionString);
                services.AddInquiryMySql(_connectionString);
                break;
            }

            case "SqlServer":
            {
                var c = new MsSqlBuilder().WithImage("mcr.microsoft.com/mssql/server:2022-latest").Build();
                await c.StartAsync();
                _container = c;
                _connectionString = c.GetConnectionString();
                ddl = "CREATE TABLE Shippers (ShipperID INT IDENTITY PRIMARY KEY, CompanyName NVARCHAR(40) NOT NULL, Phone NVARCHAR(40));";
                _open = () => new SqlConnection(_connectionString);
                services.AddInquirySqlServer(_connectionString);
                break;
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(Dialect), Dialect, "Unsupported dialect.");
        }

        _services = services.BuildServiceProvider();
        _inquiry = _services.GetRequiredService<IInquiry>();

        await using var connection = _open();
        await connection.OpenAsync();
        await using (var create = connection.CreateCommand()) { create.CommandText = ddl; await create.ExecuteNonQueryAsync(); }
        await using (var insert = connection.CreateCommand())
        {
            insert.CommandText = "INSERT INTO Shippers (CompanyName, Phone) VALUES (@c, @p)";
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

    [BenchmarkCategory("SelectAll"), Benchmark(Baseline = true)]
    public async Task<int> SelectAll_Dapper()
    {
        await using var connection = _open();
        await connection.OpenAsync();
        return (await connection.QueryAsync<Shipper>(SelectAllSql)).AsList().Count;
    }

    [BenchmarkCategory("SelectAll"), Benchmark]
    public async Task<int> SelectAll_Inquiry()
        => (await _inquiry.QueryListAsync<Shipper>(SelectAllSql)).Count;

    [BenchmarkCategory("SelectByKey"), Benchmark(Baseline = true)]
    public async Task<Shipper?> SelectByKey_Dapper()
    {
        await using var connection = _open();
        await connection.OpenAsync();
        return await connection.QuerySingleOrDefaultAsync<Shipper>(SelectByKeySql, new { id = TargetShipperId });
    }

    [BenchmarkCategory("SelectByKey"), Benchmark]
    public async Task<Shipper?> SelectByKey_Inquiry()
        => await _inquiry.QuerySingleOrDefaultAsync<Shipper>(SelectByKeySql, new { id = TargetShipperId });
}
