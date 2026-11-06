using System.Data;
using System.Diagnostics.CodeAnalysis;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Dapper;
using Inquiry.Benchmarks.PostgreSql.Ef;
using Inquiry.Benchmarks.PostgreSql.Ef.CompiledModels;
using Inquiry.Benchmarks.PostgreSql.LinqToDb;
using Inquiry.Benchmarks.PostgreSql.RepoDB;
using Inquiry.Northwind.Models;
using LinqToDB;
using LinqToDB.Data;
using LinqToDB.DataProvider.PostgreSQL;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;
using RepoDb;
using L2Async = LinqToDB.Async.AsyncExtensions;

namespace Inquiry.Benchmarks.PostgreSql;

/// <summary>
/// Shipper point read and full read on PostgreSQL against every competitor, including the source-generated
/// and tuned configurations: Dapper AOT, EF Core with compiled queries, a pooled context and a compiled
/// model, and linq2db compiled queries. The plain ADO.NET, Dapper and EF Core legs are the same as in
/// <see cref="ShipperBenchmarks"/>. Requires Docker.
/// </summary>
/// <remarks>
/// The tuned EF Core legs reuse pooled contexts on purpose. The plain EF Core legs keep the non-pooled
/// factory from <see cref="PostgreSqlBenchmarkDatabase"/>. The container is shared by the whole process, so
/// under <c>--inProcess</c> run this class with its own filter: other classes seed one row or insert rows.
/// </remarks>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class ShipperReadCompetitorBenchmarks
{
    private const int TargetShipperId = 1;
    private const string SelectAllSql = "SELECT \"ShipperID\", \"CompanyName\", \"Phone\" FROM \"Shippers\"";
    private const string SelectByKeySql = SelectAllSql + " WHERE \"ShipperID\" = @id";
    private const string AotJustification = "The AOT leg must do the same work as the plain Dapper leg. Only the interception differs.";

    private static readonly Func<PgShipperContext, int, Task<PgEfShipper?>> _efShipperByKey =
        EF.CompileAsyncQuery((PgShipperContext ctx, int id) =>
            ctx.Shippers.AsNoTracking().FirstOrDefault(s => s.ShipperID == id));

    private static readonly Func<PgShipperContext, IAsyncEnumerable<PgEfShipper>> _efAllShippers =
        EF.CompileAsyncQuery((PgShipperContext ctx) => ctx.Shippers.AsNoTracking());

    private static readonly Func<DataConnection, int, CancellationToken, Task<L2Shipper?>> _l2ShipperByKey =
        CompiledQuery.Compile((DataConnection db, int id, CancellationToken ct) =>
            L2Async.FirstOrDefaultAsync(db.GetTable<L2Shipper>().Where(s => s.ShipperID == id), ct));

    private static readonly Func<DataConnection, CancellationToken, Task<List<L2Shipper>>> _l2AllShippers =
        CompiledQuery.Compile((DataConnection db, CancellationToken ct) =>
            L2Async.ToListAsync(db.GetTable<L2Shipper>(), ct));

    private PostgreSqlBenchmarkDatabase _db = null!;
    private string _connectionString = null!;
    private DataOptions _linqToDbOptions = null!;
    private PooledDbContextFactory<PgShipperContext> _tunedContextFactory = null!;

    [SuppressMessage("Usage", "BDN1302", Justification = "The single value keeps the parameter in the benchmark result names, so results stay comparable across runs.")]
    [Params(1000)]
    public int Rows { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        _db = PostgreSqlBenchmarkDatabase.CreateAsync(Rows).GetAwaiter().GetResult();
        _connectionString = _db.ConnectionString;
        _linqToDbOptions = new DataOptions().UsePostgreSQL(_connectionString, PostgreSQLVersion.v15);
        GlobalConfiguration.Setup().UsePostgreSql();
        _tunedContextFactory = new PooledDbContextFactory<PgShipperContext>(
            new DbContextOptionsBuilder<PgShipperContext>()
                .UseNpgsql(_connectionString)
                .UseModel(PgShipperContextModel.Instance)
                .Options);
        VerifyLegsAgreeAsync().GetAwaiter().GetResult();
    }

    [GlobalCleanup]
    public void GlobalCleanup() => _db.DisposeAsync().AsTask().GetAwaiter().GetResult();

    private static Shipper ReadShipper(System.Data.Common.DbDataReader reader) => new Shipper
    {
        ShipperID = reader.GetInt32(0),
        CompanyName = reader.GetString(1),
        Phone = reader.IsDBNull(2) ? null : reader.GetString(2),
    };

    private NpgsqlConnection OpenConnection() => new NpgsqlConnection(_connectionString);

    // A wrong leg would otherwise only show up as a suspiciously fast result.
    private async Task VerifyLegsAgreeAsync()
    {
        int[] counts =
        [
            await SelectAll_AdoNet(), await SelectAll_Dapper(), await SelectAll_DapperAot(), await SelectAll_EfCore(),
            await SelectAll_EfCoreTuned(), await SelectAll_LinqToDb(), await SelectAll_LinqToDbCompiled(),
            await SelectAll_RepoDb(), await SelectAll_Inquiry(),
        ];
        if (counts.Any(count => count != Rows))
        {
            throw new InvalidOperationException(
                $"SelectAll legs returned [{string.Join(", ", counts)}]. Expected {Rows} each. If another benchmark class " +
                "ran first in this process, it seeded or changed the shared container. Run this class with its own filter.");
        }

        var expected = await SelectByKey_AdoNet() ?? throw new InvalidOperationException("The ADO.NET point read found no row.");
        (int? Id, string Company, string? Phone)[] rows =
        [
            ToTuple(await SelectByKey_Dapper()), ToTuple(await SelectByKey_DapperAot()),
            await SelectByKey_EfCore() is { } ef ? (ef.ShipperID, ef.CompanyName, ef.Phone) : default,
            await SelectByKey_EfCoreTuned() is { } eft ? (eft.ShipperID, eft.CompanyName, eft.Phone) : default,
            await SelectByKey_LinqToDb() is { } l2 ? (l2.ShipperID, l2.CompanyName, l2.Phone) : default,
            await SelectByKey_LinqToDbCompiled() is { } l2c ? (l2c.ShipperID, l2c.CompanyName, l2c.Phone) : default,
            await SelectByKey_RepoDb() is { } rd ? (rd.ShipperID, rd.CompanyName, rd.Phone) : default,
            ToTuple(await SelectByKey_Inquiry()),
        ];
        if (rows.Any(row => row != ToTuple(expected)))
        {
            throw new InvalidOperationException($"SelectByKey legs disagree: [{string.Join(", ", rows)}]. Expected {ToTuple(expected)}.");
        }

        static (int?, string, string?) ToTuple(Shipper? s) => s is null ? default : (s.ShipperID, s.CompanyName, s.Phone);
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
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleResult | CommandBehavior.SequentialAccess);
        while (await reader.ReadAsync())
        {
            list.Add(ReadShipper(reader));
        }

        return list.Count;
    }

    [BenchmarkCategory("SelectAll"), Benchmark]
    public async Task<int> SelectAll_Dapper()
    {
        await using var connection = OpenConnection();
        await connection.OpenAsync();
        var list = (await connection.QueryAsync<Shipper>(SelectAllSql)).AsList();
        return list.Count;
    }

    [BenchmarkCategory("SelectAll"), Benchmark]
    [DapperAot]
    [SuppressMessage("Major Code Smell", "S4144:Methods should not have identical implementations", Justification = AotJustification)]
    public async Task<int> SelectAll_DapperAot()
    {
        await using var connection = OpenConnection();
        await connection.OpenAsync();
        var list = (await connection.QueryAsync<Shipper>(SelectAllSql)).AsList();
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
    public async Task<int> SelectAll_EfCoreTuned()
    {
        await using var ctx = await _tunedContextFactory.CreateDbContextAsync();
        var list = new List<PgEfShipper>();
        await foreach (var shipper in _efAllShippers(ctx))
        {
            list.Add(shipper);
        }

        return list.Count;
    }

    [BenchmarkCategory("SelectAll"), Benchmark]
    public async Task<int> SelectAll_LinqToDb()
    {
        await using var dc = new DataConnection(_linqToDbOptions);
        var list = await L2Async.ToListAsync(dc.GetTable<L2Shipper>());
        return list.Count;
    }

    [BenchmarkCategory("SelectAll"), Benchmark]
    public async Task<int> SelectAll_LinqToDbCompiled()
    {
        await using var dc = new DataConnection(_linqToDbOptions);
        var list = await _l2AllShippers(dc, CancellationToken.None);
        return list.Count;
    }

    [BenchmarkCategory("SelectAll"), Benchmark]
    public async Task<int> SelectAll_RepoDb()
    {
        await using var connection = OpenConnection();
        await connection.OpenAsync();
        var list = (await DbConnectionExtension.QueryAllAsync<RdShipper>(connection)).AsList();
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
        await using var connection = OpenConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = SelectByKeySql;
        command.Parameters.AddWithValue("id", TargetShipperId);
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleResult | CommandBehavior.SingleRow | CommandBehavior.SequentialAccess);
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
    [DapperAot]
    [SuppressMessage("Major Code Smell", "S4144:Methods should not have identical implementations", Justification = AotJustification)]
    public async Task<Shipper?> SelectByKey_DapperAot()
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
    public async Task<PgEfShipper?> SelectByKey_EfCoreTuned()
    {
        await using var ctx = await _tunedContextFactory.CreateDbContextAsync();
        return await _efShipperByKey(ctx, TargetShipperId);
    }

    [BenchmarkCategory("SelectByKey"), Benchmark]
    public async Task<L2Shipper?> SelectByKey_LinqToDb()
    {
        await using var dc = new DataConnection(_linqToDbOptions);
        return await L2Async.FirstOrDefaultAsync(dc.GetTable<L2Shipper>(), s => s.ShipperID == TargetShipperId);
    }

    [BenchmarkCategory("SelectByKey"), Benchmark]
    public async Task<L2Shipper?> SelectByKey_LinqToDbCompiled()
    {
        await using var dc = new DataConnection(_linqToDbOptions);
        return await _l2ShipperByKey(dc, TargetShipperId, CancellationToken.None);
    }

    [BenchmarkCategory("SelectByKey"), Benchmark]
    public async Task<RdShipper?> SelectByKey_RepoDb()
    {
        await using var connection = OpenConnection();
        await connection.OpenAsync();
        return (await DbConnectionExtension.QueryAsync<RdShipper>(connection, TargetShipperId)).FirstOrDefault();
    }

    [BenchmarkCategory("SelectByKey"), Benchmark]
    public async Task<Shipper?> SelectByKey_Inquiry()
        => await _db.Shippers.SelectByKeyAsync(TargetShipperId);
}
