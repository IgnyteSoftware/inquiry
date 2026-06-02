using Inquiry.Benchmarks.Oracle.Ef;
using Inquiry.DependencyInjection;
using Inquiry.Northwind;
using Inquiry.Northwind.Stores;
using Inquiry.Oracle.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Oracle.ManagedDataAccess.Client;
using Testcontainers.Oracle;

namespace Inquiry.Benchmarks.Oracle;

/// <summary>
/// Process-wide Oracle Testcontainer + DI for the benchmark suite. The container is the expensive
/// resource (Oracle is especially slow to start), so it is started <b>once per process</b> and
/// reused by every benchmark method (BenchmarkDotNet must run <c>--inProcess</c>); the seed runs
/// once. Read benchmarks are non-mutating, and the write benchmarks run after them (declared order,
/// see <c>[Orderer]</c> on the benchmark class) and target a stable key, so a per-method reseed is
/// unnecessary. The container is torn down at process exit (and by the Testcontainers reaper as a
/// backstop). EF uses a non-pooled factory so it pays per-operation context construction — the same
/// lifecycle ADO, Dapper, and Inquiry each take (fresh connection per call).
/// </summary>
public sealed class OracleBenchmarkDatabase : IAsyncDisposable
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static OracleContainer? _container;
    private static ServiceProvider? _services;
    private static string? _connectionString;
    private static IDbContextFactory<OracleShipperContext>? _dbContextFactory;

    private OracleBenchmarkDatabase(int rowCount) => RowCount = rowCount;

    public string ConnectionString => _connectionString!;

    /// <summary>Number of Shipper rows seeded into the shared database.</summary>
    public int RowCount { get; }

    public IDbContextFactory<OracleShipperContext> DbContextFactory => _dbContextFactory!;

    public ShipperStore Shippers => _services!.GetRequiredService<ShipperStore>();

    /// <summary>
    /// Returns a handle over the process-wide shared container, starting + seeding it on first call.
    /// </summary>
    public static async Task<OracleBenchmarkDatabase> CreateAsync(int seedRows)
    {
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_container is null)
            {
                // Use the XE image whose service name (XEPDB1) matches what Testcontainers.Oracle bakes
                // into its connection string. The oracle-free image serves FREEPDB1 instead, so its
                // generated connection string fails with ORA-12514 (service not known).
                var container = new OracleBuilder()
                    .WithImage("gvenzl/oracle-xe:21-slim-faststart")
                    .Build();
                await container.StartAsync().ConfigureAwait(false);

                // The container's default user is limited; rewrite to SYSTEM for DDL access. In the
                // gvenzl image the SYSTEM password equals the configured ORACLE_PASSWORD, so only the
                // user id is swapped.
                var adminConnectionString = new OracleConnectionStringBuilder(container.GetConnectionString())
                {
                    UserID = "SYSTEM",
                }.ToString();

                // The faststart image's listener accepts connections a moment before the database
                // service is registered; poll until the service is actually serving.
                await WaitForServiceAsync(adminConnectionString).ConfigureAwait(false);

                // Apply the Northwind DDL statement-by-statement — Oracle has no multi-statement batch.
                await using (var connection = new OracleConnection(adminConnectionString))
                {
                    await connection.OpenAsync().ConfigureAwait(false);
                    foreach (var statement in SplitStatements(NorthwindSchema.OracleDdl))
                    {
                        await using var command = connection.CreateCommand();
                        command.CommandText = statement;
                        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
                    }
                }

                var services = new ServiceCollection()
                    .AddInquiry()
                    .AddInquiryOracle(adminConnectionString)
                    // Non-pooled: each CreateDbContext builds a fresh context, so EF pays per-operation
                    // setup the same way ADO/Dapper/Inquiry each open a fresh connection per call.
                    .AddDbContextFactory<OracleShipperContext>(options => options.UseOracle(adminConnectionString))
                    .BuildServiceProvider();

                await SeedAsync(adminConnectionString, seedRows).ConfigureAwait(false);

                _connectionString = adminConnectionString;
                _services = services;
                _dbContextFactory = services.GetRequiredService<IDbContextFactory<OracleShipperContext>>();
                _container = container;

                AppDomain.CurrentDomain.ProcessExit += static (_, _) =>
                {
                    try
                    {
                        _services?.Dispose();
                        // Force-close pooled connections before disposing the container.
                        OracleConnection.ClearAllPools();
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

        return new OracleBenchmarkDatabase(seedRows);
    }

    private static async Task WaitForServiceAsync(string connectionString)
    {
        const int maxAttempts = 30;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var connection = new OracleConnection(connectionString);
                await connection.OpenAsync().ConfigureAwait(false);
                await using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT 1 FROM dual";
                await cmd.ExecuteScalarAsync().ConfigureAwait(false);
                return;
            }
            catch (OracleException) when (attempt < maxAttempts)
            {
                await Task.Delay(2000).ConfigureAwait(false);
            }
        }
    }

    private static IEnumerable<string> SplitStatements(string ddl)
    {
        foreach (var raw in ddl.Split(';'))
        {
            var statement = raw.Trim();
            if (statement.Length > 0)
            {
                yield return statement;
            }
        }
    }

    private static async Task SeedAsync(string connectionString, int rowCount)
    {
        await using var connection = new OracleConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var tx = await connection.BeginTransactionAsync().ConfigureAwait(false);

        // Shippers — IDENTITY PK; must not supply ShipperID on insert.
        // Oracle uses :name parameters; BindByName = true for named binding.
        var oracleTx = (OracleTransaction)tx;
        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = oracleTx;
            insert.BindByName = true;
            insert.CommandText = "INSERT INTO SHIPPERS (COMPANYNAME, PHONE) VALUES (:company, :phone)";
            var pCompany = insert.Parameters.Add("company", OracleDbType.Varchar2);
            var pPhone   = insert.Parameters.Add("phone",   OracleDbType.Varchar2);
            await insert.PrepareAsync().ConfigureAwait(false);
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
