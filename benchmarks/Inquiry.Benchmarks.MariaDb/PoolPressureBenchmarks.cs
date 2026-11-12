using System.Data;
using System.Diagnostics.CodeAnalysis;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Dapper;
using Inquiry.Benchmarks.MariaDb.Ef;
using Inquiry.Benchmarks.MariaDb.Ef.CompiledModels;
using Inquiry.Benchmarks.MariaDb.LinqToDb;
using Inquiry.Benchmarks.MariaDb.RepoDB;
using Inquiry.DependencyInjection;
using Inquiry.MariaDb.DependencyInjection;
using Inquiry.Northwind.Models;
using Inquiry.Northwind.Stores;
using LinqToDB;
using LinqToDB.Data;
using LinqToDB.DataProvider.MySql;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using RepoDb;
using L2Async = LinqToDB.Async.AsyncExtensions;

namespace Inquiry.Benchmarks.MariaDb;

/// <summary>
/// Connection-pool pressure on MariaDB: <see cref="Callers"/> concurrent callers each run one shipper point
/// read through a pool that holds at most <see cref="MaxPoolSize"/> connections. With 2 connections the
/// callers wait for the pool. With 16 no caller waits, which gives the cost of the same calls without
/// pressure. Requires Docker.
/// </summary>
/// <remarks>
/// Every leg opens and closes its own connection or context for each call, and every leg uses the same
/// connection string with the same cap. A library that builds its own data source from that string has
/// its own pool with that cap. The legs are the point reads of
/// <see cref="ShipperReadCompetitorBenchmarks"/>. The Inquiry leg has its own service provider, because
/// the fixture's provider uses the uncapped connection string. The tiers are small on purpose: the idle
/// connections of earlier legs stay open, and the PostgreSQL container accepts only 100 connections.
/// </remarks>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
[SuppressMessage("Design", "CA1001", Justification = "BenchmarkDotNet disposes these fields in the [GlobalCleanup] method.")]
public class PoolPressureBenchmarks
{
    private const int Callers = 16;
    private const int TargetShipperId = 1;
    private const string SelectAllSql = "SELECT `ShipperID`, `CompanyName`, `Phone` FROM `Shippers`";
    private const string SelectByKeySql = SelectAllSql + " WHERE `ShipperID` = @id";
    private const string AotJustification = "The AOT leg must do the same work as the plain Dapper leg. Only the interception differs.";

    private static readonly Func<MariaDbShipperContext, int, Task<MariaDbEfShipper?>> _efShipperByKey =
        EF.CompileAsyncQuery((MariaDbShipperContext ctx, int id) =>
            ctx.Shippers.AsNoTracking().FirstOrDefault(s => s.ShipperID == id));

    private static readonly Func<DataConnection, int, CancellationToken, Task<L2Shipper?>> _l2ShipperByKey =
        CompiledQuery.Compile((DataConnection db, int id, CancellationToken ct) =>
            L2Async.FirstOrDefaultAsync(db.GetTable<L2Shipper>().Where(s => s.ShipperID == id), ct));

    private MariaDbBenchmarkDatabase _db = null!;
    private string _connectionString = null!;
    private DataOptions _linqToDbOptions = null!;
    private PooledDbContextFactory<MariaDbShipperContext> _tunedContextFactory = null!;
    private DbContextOptions<MariaDbShipperContext> _contextOptions = null!;
    private ServiceProvider _services = null!;
    private ShipperStore _shippers = null!;

    /// <summary>Connections that the pool may hold: fewer than the callers (2) or one for each caller (16).</summary>
    [Params(2, Callers)] public int MaxPoolSize { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        _db = MariaDbBenchmarkDatabase.CreateAsync(1000).GetAwaiter().GetResult();
        _connectionString = new MySqlConnectionStringBuilder(_db.ConnectionString) { MaximumPoolSize = (uint)MaxPoolSize }.ConnectionString;
        _linqToDbOptions = new DataOptions().UseMySql(_connectionString, MySqlVersion.MariaDB10, MySqlProvider.MySqlConnector);
        GlobalConfiguration.Setup().UseMySqlConnector();
        _tunedContextFactory = new PooledDbContextFactory<MariaDbShipperContext>(
            new DbContextOptionsBuilder<MariaDbShipperContext>()
                .UseMySql(_connectionString, new MariaDbServerVersion(new Version(11, 4, 0)))
                .UseModel(MariaDbShipperContextModel.Instance)
                .Options);
        _contextOptions = new DbContextOptionsBuilder<MariaDbShipperContext>()
            .UseMySql(_connectionString, new MariaDbServerVersion(new Version(11, 4, 0)))
            .Options;
        _services = new ServiceCollection()
            .AddInquiry()
            .AddInquiryGeneratedStores()
            .AddInquiryMariaDb(_connectionString)
            .BuildServiceProvider();
        _shippers = _services.GetRequiredService<ShipperStore>();
        VerifyLegsAgreeAsync().GetAwaiter().GetResult();
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _services.Dispose();
        _db.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private static Shipper ReadShipper(System.Data.Common.DbDataReader reader) => new Shipper
    {
        ShipperID = reader.GetInt32(0),
        CompanyName = reader.GetString(1),
        Phone = reader.IsDBNull(2) ? null : reader.GetString(2),
    };

    private MySqlConnection OpenConnection() => new MySqlConnection(_connectionString);

    // A wrong leg would otherwise only show up as a suspiciously fast result. The single reads also come
    // first because linq2db 6.5 can fail when its first queries in a process run at the same time.
    private async Task VerifyLegsAgreeAsync()
    {
        var expected = await PointRead_AdoNet() ?? throw new InvalidOperationException("The ADO.NET point read found no row.");
        (int? Id, string Company, string? Phone)[] rows =
        [
            ToTuple(await PointRead_Dapper()), ToTuple(await PointRead_DapperAot()),
            await PointRead_EfCore() is { } ef ? (ef.ShipperID, ef.CompanyName, ef.Phone) : default,
            await PointRead_EfCoreTuned() is { } eft ? (eft.ShipperID, eft.CompanyName, eft.Phone) : default,
            await PointRead_LinqToDb() is { } l2 ? (l2.ShipperID, l2.CompanyName, l2.Phone) : default,
            await PointRead_LinqToDbCompiled() is { } l2c ? (l2c.ShipperID, l2c.CompanyName, l2c.Phone) : default,
            await PointRead_RepoDb() is { } rd ? (rd.ShipperID, rd.CompanyName, rd.Phone) : default,
            ToTuple(await PointRead_Inquiry()),
        ];
        if (rows.Any(row => row != ToTuple(expected)))
        {
            throw new InvalidOperationException($"Point reads disagree: [{string.Join(", ", rows)}]. Expected {ToTuple(expected)}.");
        }

        int[] found =
        [
            await PoolPressure_AdoNet(), await PoolPressure_Dapper(), await PoolPressure_DapperAot(), await PoolPressure_EfCore(),
            await PoolPressure_EfCoreTuned(), await PoolPressure_LinqToDb(), await PoolPressure_LinqToDbCompiled(),
            await PoolPressure_RepoDb(), await PoolPressure_Inquiry(),
        ];
        if (found.Any(count => count != Callers))
        {
            throw new InvalidOperationException($"PoolPressure legs found [{string.Join(", ", found)}] rows. Expected {Callers} each.");
        }

        static (int?, string, string?) ToTuple(Shipper? s) => s is null ? default : (s.ShipperID, s.CompanyName, s.Phone);
    }

    private static async Task<int> RunCallersAsync<T>(Func<Task<T?>> pointRead)
        where T : class
    {
        var tasks = new Task<T?>[Callers];
        for (var i = 0; i < Callers; i++)
        {
            tasks[i] = Task.Run(pointRead);
        }

        var rows = await Task.WhenAll(tasks);
        return rows.Count(row => row is not null);
    }

    // ---- PoolPressure -------------------------------------------------------------------

    [BenchmarkCategory("PoolPressure"), Benchmark(Baseline = true)]
    public Task<int> PoolPressure_AdoNet() => RunCallersAsync(PointRead_AdoNet);

    [BenchmarkCategory("PoolPressure"), Benchmark]
    public Task<int> PoolPressure_Dapper() => RunCallersAsync(PointRead_Dapper);

    [BenchmarkCategory("PoolPressure"), Benchmark]
    public Task<int> PoolPressure_DapperAot() => RunCallersAsync(PointRead_DapperAot);

    [BenchmarkCategory("PoolPressure"), Benchmark]
    public Task<int> PoolPressure_EfCore() => RunCallersAsync(PointRead_EfCore);

    [BenchmarkCategory("PoolPressure"), Benchmark]
    public Task<int> PoolPressure_EfCoreTuned() => RunCallersAsync(PointRead_EfCoreTuned);

    [BenchmarkCategory("PoolPressure"), Benchmark]
    public Task<int> PoolPressure_LinqToDb() => RunCallersAsync(PointRead_LinqToDb);

    [BenchmarkCategory("PoolPressure"), Benchmark]
    public Task<int> PoolPressure_LinqToDbCompiled() => RunCallersAsync(PointRead_LinqToDbCompiled);

    [BenchmarkCategory("PoolPressure"), Benchmark]
    public Task<int> PoolPressure_RepoDb() => RunCallersAsync(PointRead_RepoDb);

    [BenchmarkCategory("PoolPressure"), Benchmark]
    public Task<int> PoolPressure_Inquiry() => RunCallersAsync(PointRead_Inquiry);

    // ---- One caller ---------------------------------------------------------------------

    private async Task<Shipper?> PointRead_AdoNet()
    {
        await using var connection = OpenConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = SelectByKeySql;
        command.Parameters.AddWithValue("id", TargetShipperId);
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleResult | CommandBehavior.SingleRow | CommandBehavior.SequentialAccess);
        return await reader.ReadAsync() ? ReadShipper(reader) : null;
    }

    private async Task<Shipper?> PointRead_Dapper()
    {
        await using var connection = OpenConnection();
        await connection.OpenAsync();
        return await connection.QuerySingleOrDefaultAsync<Shipper>(SelectByKeySql, new { id = TargetShipperId });
    }

    [DapperAot]
    [SuppressMessage("Major Code Smell", "S4144:Methods should not have identical implementations", Justification = AotJustification)]
    private async Task<Shipper?> PointRead_DapperAot()
    {
        await using var connection = OpenConnection();
        await connection.OpenAsync();
        return await connection.QuerySingleOrDefaultAsync<Shipper>(SelectByKeySql, new { id = TargetShipperId });
    }

    private async Task<MariaDbEfShipper?> PointRead_EfCore()
    {
        await using var ctx = new MariaDbShipperContext(_contextOptions);
        return await ctx.Shippers.AsNoTracking().FirstOrDefaultAsync(s => s.ShipperID == TargetShipperId);
    }

    private async Task<MariaDbEfShipper?> PointRead_EfCoreTuned()
    {
        await using var ctx = await _tunedContextFactory.CreateDbContextAsync();
        return await _efShipperByKey(ctx, TargetShipperId);
    }

    private async Task<L2Shipper?> PointRead_LinqToDb()
    {
        await using var dc = new DataConnection(_linqToDbOptions);
        return await L2Async.FirstOrDefaultAsync(dc.GetTable<L2Shipper>(), s => s.ShipperID == TargetShipperId);
    }

    private async Task<L2Shipper?> PointRead_LinqToDbCompiled()
    {
        await using var dc = new DataConnection(_linqToDbOptions);
        return await _l2ShipperByKey(dc, TargetShipperId, CancellationToken.None);
    }

    private async Task<RdShipper?> PointRead_RepoDb()
    {
        await using var connection = OpenConnection();
        await connection.OpenAsync();
        return (await DbConnectionExtension.QueryAsync<RdShipper>(connection, TargetShipperId)).FirstOrDefault();
    }

    private async Task<Shipper?> PointRead_Inquiry()
        => await _shippers.SelectByKeyAsync(TargetShipperId);
}
