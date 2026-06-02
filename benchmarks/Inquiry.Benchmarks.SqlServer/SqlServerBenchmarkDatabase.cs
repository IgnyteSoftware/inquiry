using Inquiry.Benchmarks.SqlServer.Ef;
using Inquiry.DependencyInjection;
using Inquiry.Northwind;
using Inquiry.Northwind.Stores;
using Inquiry.SqlServer.DependencyInjection;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MsSql;

namespace Inquiry.Benchmarks.SqlServer;

/// <summary>
/// Process-wide SQL Server Testcontainer + DI for the benchmark suite. The container is the
/// expensive resource, so it is started <b>once per process</b> and reused by every benchmark
/// method (BenchmarkDotNet must run <c>--inProcess</c>); the seed runs once. Read benchmarks are
/// non-mutating, and the write benchmarks run after them (declared order, see <c>[Orderer]</c> on
/// the benchmark class) and target a stable key, so a per-method reseed is unnecessary. The
/// container is torn down at process exit (and by the Testcontainers reaper as a backstop). EF uses
/// a non-pooled factory so it pays per-operation context construction — the same lifecycle ADO,
/// Dapper, and Inquiry each take (fresh connection per call).
/// </summary>
public sealed class SqlServerBenchmarkDatabase : IAsyncDisposable
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static MsSqlContainer? _container;
    private static ServiceProvider? _services;
    private static string? _connectionString;
    private static IDbContextFactory<SqlServerShipperContext>? _dbContextFactory;

    private SqlServerBenchmarkDatabase(int rowCount) => RowCount = rowCount;

    public string ConnectionString => _connectionString!;

    /// <summary>Number of Shipper rows seeded into the shared database.</summary>
    public int RowCount { get; }

    public IDbContextFactory<SqlServerShipperContext> DbContextFactory => _dbContextFactory!;

    public ShipperStore Shippers => _services!.GetRequiredService<ShipperStore>();

    /// <summary>
    /// Returns a handle over the process-wide shared container, starting + seeding it on first call.
    /// </summary>
    public static async Task<SqlServerBenchmarkDatabase> CreateAsync(int seedRows)
    {
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_container is null)
            {
                var container = new MsSqlBuilder()
                    .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
                    .Build();
                await container.StartAsync().ConfigureAwait(false);
                var connectionString = container.GetConnectionString();

                await using (var connection = new SqlConnection(connectionString))
                {
                    await connection.OpenAsync().ConfigureAwait(false);
                    await using var command = connection.CreateCommand();
                    command.CommandText = NorthwindSchema.SqlServerDdl;
                    await command.ExecuteNonQueryAsync().ConfigureAwait(false);
                }

                var services = new ServiceCollection()
                    .AddInquiry()
                    .AddInquirySqlServer(connectionString)
                    // Non-pooled: each CreateDbContext builds a fresh context, so EF pays per-operation
                    // setup the same way ADO/Dapper/Inquiry each open a fresh connection per call.
                    .AddDbContextFactory<SqlServerShipperContext>(options => options.UseSqlServer(connectionString))
                    .BuildServiceProvider();

                await SeedAsync(connectionString, seedRows).ConfigureAwait(false);

                _connectionString = connectionString;
                _services = services;
                _dbContextFactory = services.GetRequiredService<IDbContextFactory<SqlServerShipperContext>>();
                _container = container;

                AppDomain.CurrentDomain.ProcessExit += static (_, _) =>
                {
                    try
                    {
                        _services?.Dispose();
                        _container?.DisposeAsync().AsTask().GetAwaiter().GetResult();
                    }
                    catch { /* best-effort; the Testcontainers reaper cleans up the container too */ }
                };
            }
        }
        finally
        {
            Gate.Release();
        }

        return new SqlServerBenchmarkDatabase(seedRows);
    }

    private static async Task SeedAsync(string connectionString, int rowCount)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var tx = await connection.BeginTransactionAsync().ConfigureAwait(false);

        // Shippers — IDENTITY PK; SQL Server uses @name parameters. ShipperID is not supplied.
        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = (SqlTransaction)tx;
            insert.CommandText = "INSERT INTO Shippers (CompanyName, Phone) VALUES (@company, @phone);";
            var pCompany = insert.Parameters.Add("@company", System.Data.SqlDbType.NVarChar, 40);
            var pPhone   = insert.Parameters.Add("@phone",   System.Data.SqlDbType.NVarChar, -1);
            for (int i = 0; i < rowCount; i++)
            {
                pCompany.Value = $"Shipper {i}";
                pPhone.Value   = $"555-{i:0000}";
                await insert.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }

        await tx.CommitAsync().ConfigureAwait(false);
    }

    /// <summary>No-op: the shared container outlives individual benchmark methods (see class remarks).</summary>
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
