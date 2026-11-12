using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Dapper;
using Inquiry.Benchmarks.PostgreSql.Ef;
using Inquiry.Benchmarks.PostgreSql.Ef.CompiledModels;
using Inquiry.Benchmarks.PostgreSql.LinqToDb;
using Inquiry.Benchmarks.PostgreSql.RepoDB;
using Inquiry.DependencyInjection;
using Inquiry.Northwind.Models;
using Inquiry.Northwind.Stores;
using Inquiry.PostgreSql.DependencyInjection;
using LinqToDB;
using LinqToDB.Data;
using LinqToDB.DataProvider.PostgreSQL;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using RepoDb;
using L2Async = LinqToDB.Async.AsyncExtensions;

namespace Inquiry.Benchmarks.PostgreSql;

/// <summary>
/// Real process cold start on PostgreSQL. The driver (<see cref="DriverSwitch"/>) starts the database once.
/// It then starts this program again for each measurement, as a probe (<see cref="ProbeSwitch"/>) that
/// runs one shipper point read with one library and exits. The time is from process launch to process
/// exit, so it includes runtime start, assembly loading, JIT and the first connection.
/// </summary>
/// <remarks>
/// A probe sets up only the library of its leg, inside the measured process. The <c>Startup</c> leg makes
/// no database call and shows the cost of the process alone. This is not a BenchmarkDotNet class:
/// BenchmarkDotNet measures inside a process that has already started.
/// </remarks>
public static class ProcessColdStart
{
    public const string DriverSwitch = "--process-cold-start";
    public const string ProbeSwitch = "--process-cold-start-probe";

    private const int TargetShipperId = 1;
    private const string SelectByKeySql = "SELECT \"ShipperID\", \"CompanyName\", \"Phone\" FROM \"Shippers\" WHERE \"ShipperID\" = @id";
    private const string AotJustification = "The AOT leg must do the same work as the plain Dapper leg. Only the interception differs.";

    private static readonly string[] _legs =
    [
        "Startup", "AdoNet", "Dapper", "DapperAot", "EfCore", "EfCoreTuned", "LinqToDb", "LinqToDbCompiled", "RepoDb", "Inquiry",
    ];

    /// <summary>Seeds the database and measures every leg. <paramref name="arguments"/> may give the launches per leg.</summary>
    public static async Task<int> RunAsync(string[] arguments)
    {
        var launches = arguments is [var count] ? int.Parse(count, CultureInfo.InvariantCulture) : 20;
        await using var db = await PostgreSqlBenchmarkDatabase.CreateAsync(1000);
        return ProcessColdStartRunner.Run(typeof(ProcessColdStart).Assembly.Location, ProbeSwitch, _legs, "AdoNet", launches, db.ConnectionString);
    }

    /// <summary>Runs one leg in this process. Returns 0 when the leg read the row.</summary>
    public static async Task<int> ProbeAsync(string leg)
    {
        var connectionString = ProcessColdStartRunner.ConnectionString;
        object? row = leg switch
        {
            "Startup" => leg,
            "AdoNet" => await AdoNetAsync(connectionString),
            "Dapper" => await DapperAsync(connectionString),
            "DapperAot" => await DapperAotAsync(connectionString),
            "EfCore" => await EfCoreAsync(connectionString),
            "EfCoreTuned" => await EfCoreTunedAsync(connectionString),
            "LinqToDb" => await LinqToDbAsync(connectionString),
            "LinqToDbCompiled" => await LinqToDbCompiledAsync(connectionString),
            "RepoDb" => await RepoDbAsync(connectionString),
            "Inquiry" => await InquiryAsync(connectionString),
            _ => throw new ArgumentException($"Unknown leg '{leg}'.", nameof(leg)),
        };
        return row is null ? 1 : 0;
    }

    private static async Task<Shipper?> AdoNetAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = SelectByKeySql;
        command.Parameters.AddWithValue("id", TargetShipperId);
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleResult | CommandBehavior.SingleRow | CommandBehavior.SequentialAccess);
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new Shipper { ShipperID = reader.GetInt32(0), CompanyName = reader.GetString(1), Phone = reader.IsDBNull(2) ? null : reader.GetString(2) };
    }

    private static async Task<Shipper?> DapperAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        return await connection.QuerySingleOrDefaultAsync<Shipper>(SelectByKeySql, new { id = TargetShipperId });
    }

    [DapperAot]
    [SuppressMessage("Major Code Smell", "S4144:Methods should not have identical implementations", Justification = AotJustification)]
    private static async Task<Shipper?> DapperAotAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        return await connection.QuerySingleOrDefaultAsync<Shipper>(SelectByKeySql, new { id = TargetShipperId });
    }

    private static async Task<PgEfShipper?> EfCoreAsync(string connectionString)
    {
        await using var ctx = new PgShipperContext(new DbContextOptionsBuilder<PgShipperContext>().UseNpgsql(connectionString).Options);
        return await ctx.Shippers.AsNoTracking().FirstOrDefaultAsync(s => s.ShipperID == TargetShipperId);
    }

    // The compiled model and the compiled query are the parts of the tuned configuration that change a first call.
    private static async Task<PgEfShipper?> EfCoreTunedAsync(string connectionString)
    {
        var query = EF.CompileAsyncQuery((PgShipperContext ctx, int id) => ctx.Shippers.AsNoTracking().FirstOrDefault(s => s.ShipperID == id));
        await using var context = new PgShipperContext(
            new DbContextOptionsBuilder<PgShipperContext>().UseNpgsql(connectionString).UseModel(PgShipperContextModel.Instance).Options);
        return await query(context, TargetShipperId);
    }

    private static async Task<L2Shipper?> LinqToDbAsync(string connectionString)
    {
        await using var dc = new DataConnection(new DataOptions().UsePostgreSQL(connectionString, PostgreSQLVersion.v15));
        return await L2Async.FirstOrDefaultAsync(dc.GetTable<L2Shipper>(), s => s.ShipperID == TargetShipperId);
    }

    private static async Task<L2Shipper?> LinqToDbCompiledAsync(string connectionString)
    {
        var query = CompiledQuery.Compile((DataConnection db, int id, CancellationToken ct) =>
            L2Async.FirstOrDefaultAsync(db.GetTable<L2Shipper>().Where(s => s.ShipperID == id), ct));
        await using var dc = new DataConnection(new DataOptions().UsePostgreSQL(connectionString, PostgreSQLVersion.v15));
        return await query(dc, TargetShipperId, CancellationToken.None);
    }

    private static async Task<RdShipper?> RepoDbAsync(string connectionString)
    {
        GlobalConfiguration.Setup().UsePostgreSql();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        return (await DbConnectionExtension.QueryAsync<RdShipper>(connection, TargetShipperId)).FirstOrDefault();
    }

    private static async Task<Shipper?> InquiryAsync(string connectionString)
    {
        await using var services = new ServiceCollection()
            .AddInquiry(options => options.PrepareStatements = PreparedStatementMode.None, typeof(ShipperStore).Assembly)
            .AddInquiryGeneratedStores()
            .AddInquiryPostgreSql(connectionString)
            .BuildServiceProvider();
        return await services.GetRequiredService<ShipperStore>().SelectByKeyAsync(TargetShipperId);
    }
}
