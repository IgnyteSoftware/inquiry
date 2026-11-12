using System.Data;
using System.Diagnostics;
using System.Globalization;
using Dapper;
using Inquiry.DependencyInjection;
using Inquiry.Northwind.Models;
using Inquiry.Northwind.Stores;
using Inquiry.Sqlite.DependencyInjection;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Inquiry.Benchmarks.Aot;

/// <summary>
/// NativeAOT startup, size and throughput on SQLite. Publish this program twice, as NativeAOT and as a
/// JIT build, and run <c>--compare</c> on the two publish directories. For each directory it prints the
/// size on disk, the process cold start of each leg (see <see cref="ProcessColdStartRunner"/>) and the
/// throughput of a shipper point read, which the published program measures on itself.
/// </summary>
/// <remarks>
/// BenchmarkDotNet is not used, because the same measured code must run in both builds. The legs are the
/// libraries that run under NativeAOT: ADO.NET (baseline), Dapper.AOT and the generated Inquiry store.
/// Reflection-based Dapper, EF Core, linq2db and RepoDB build their readers at run time, so they have no legs.
/// </remarks>
internal static class AotBenchmark
{
    public const string ProbeSwitch = "--probe";

    private const int Launches = 20;
    private const int Operations = 20_000;
    private const int SeedRows = 1000;
    private const int TargetShipperId = 1;
    private const string SelectByKeySql = "SELECT ShipperID, CompanyName, Phone FROM Shippers WHERE ShipperID = @id";

    private static readonly string[] _legs = ["AdoNet", "DapperAot", "Inquiry"];

    public static int Usage()
    {
        Console.Error.WriteLine("Usage: Inquiry.Benchmarks.Aot --compare <publish directory> [<publish directory> ...]");
        return 2;
    }

    /// <summary>Seeds one database, then measures the published program in each directory.</summary>
    public static async Task<int> CompareAsync(string[] directories)
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"inquiry_aot_bench_{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={databasePath}";
        await SeedAsync(connectionString);
        try
        {
            foreach (var directory in directories)
            {
                var program = Path.Combine(directory, "Inquiry.Benchmarks.Aot" + (OperatingSystem.IsWindows() ? ".exe" : string.Empty));
                var files = new DirectoryInfo(directory).GetFiles("*", SearchOption.AllDirectories);
                Console.WriteLine($"## {Path.GetFullPath(directory)}");
                Console.WriteLine();
                Console.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"Size: executable {new FileInfo(program).Length / 1048576d:F2} MB, directory {files.Sum(file => file.Length) / 1048576d:F2} MB in {files.Length} files."));
                Console.WriteLine();

                if (ProcessColdStartRunner.Run(program, ProbeSwitch, ["Startup", .. _legs], "AdoNet", Launches, connectionString) != 0)
                {
                    return 1;
                }

                Console.WriteLine();
                Console.WriteLine($"Throughput: {Operations} point reads per leg, after a warmup, measured inside the published program.");
                Console.WriteLine();
                Console.WriteLine($"| {"Leg",-18} | {"ops/s",9} | {"ns/op",9} | {"B/op",9} |");
                Console.WriteLine($"|{new string('-', 20)}|{new string('-', 10)}:|{new string('-', 10)}:|{new string('-', 10)}:|");
                foreach (var leg in _legs)
                {
                    var start = new ProcessStartInfo(program) { UseShellExecute = false };
                    start.ArgumentList.Add("--throughput");
                    start.ArgumentList.Add(leg);
                    start.ArgumentList.Add(Operations.ToString(CultureInfo.InvariantCulture));
                    start.Environment[ProcessColdStartRunner.ConnectionVariable] = connectionString;
                    using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {program}.");
                    await process.WaitForExitAsync();
                    if (process.ExitCode != 0)
                    {
                        return 1;
                    }
                }

                Console.WriteLine();
            }

            return 0;
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
        }
    }

    /// <summary>Runs one leg once in this process. Returns 0 when the leg read the row.</summary>
    public static async Task<int> ProbeAsync(string leg)
        => leg == "Startup" || await CreateLeg(leg)() is not null ? 0 : 1;

    /// <summary>Measures <paramref name="operations"/> point reads of one leg in this process and prints one table row.</summary>
    public static async Task<int> ThroughputAsync(string leg, int operations)
    {
        var pointRead = CreateLeg(leg);
        for (var i = 0; i < operations / 10; i++)
        {
            await pointRead();
        }

        var allocated = GC.GetTotalAllocatedBytes(precise: true);
        var timestamp = Stopwatch.GetTimestamp();
        for (var i = 0; i < operations; i++)
        {
            if (await pointRead() is null)
            {
                return 1;
            }
        }

        var elapsed = Stopwatch.GetElapsedTime(timestamp);
        allocated = GC.GetTotalAllocatedBytes(precise: true) - allocated;
        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"| {leg,-18} | {operations / elapsed.TotalSeconds,9:F0} | {elapsed.TotalNanoseconds / operations,9:F0} | {allocated / operations,9} |"));
        return 0;
    }

    private static Func<Task<Shipper?>> CreateLeg(string leg)
    {
        var connectionString = ProcessColdStartRunner.ConnectionString;
        switch (leg)
        {
            case "AdoNet":
                return () => AdoNetAsync(connectionString);
            case "DapperAot":
                return () => DapperAotAsync(connectionString);
            case "Inquiry":
                // The provider lives as long as the process, as it does in an application.
                var shippers = new ServiceCollection()
                    .AddInquiry()
                    .AddInquiryGeneratedStores()
                    .AddInquirySqlite(connectionString)
                    .BuildServiceProvider()
                    .GetRequiredService<ShipperStore>();
                return () => shippers.SelectByKeyAsync(TargetShipperId);
            default:
                throw new ArgumentException($"Unknown leg '{leg}'.", nameof(leg));
        }
    }

    private static async Task<Shipper?> AdoNetAsync(string connectionString)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = SelectByKeySql;
        command.Parameters.Add("@id", SqliteType.Integer).Value = TargetShipperId;
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleResult | CommandBehavior.SingleRow | CommandBehavior.SequentialAccess);
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new Shipper { ShipperID = reader.GetInt32(0), CompanyName = reader.GetString(1), Phone = reader.IsDBNull(2) ? null : reader.GetString(2) };
    }

    [DapperAot]
    private static async Task<Shipper?> DapperAotAsync(string connectionString)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        return await connection.QuerySingleOrDefaultAsync<Shipper>(SelectByKeySql, new { id = TargetShipperId });
    }

    private static async Task SeedAsync(string connectionString)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = "CREATE TABLE Shippers (ShipperID INTEGER PRIMARY KEY AUTOINCREMENT, CompanyName TEXT NOT NULL, Phone TEXT)";
        await command.ExecuteNonQueryAsync();

        command.CommandText = "INSERT INTO Shippers (CompanyName, Phone) VALUES (@company, @phone)";
        var company = command.Parameters.Add("@company", SqliteType.Text);
        var phone = command.Parameters.Add("@phone", SqliteType.Text);
        for (var i = 0; i < SeedRows; i++)
        {
            company.Value = $"Shipper {i}";
            phone.Value = $"555-{i:0000}";
            await command.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }
}
