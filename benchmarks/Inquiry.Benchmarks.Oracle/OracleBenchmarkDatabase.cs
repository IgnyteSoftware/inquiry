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
/// Per-benchmark-class scaffolding: provisions an Oracle Testcontainer, applies
/// <see cref="NorthwindSchema.OracleDdl"/> statement-by-statement (Oracle has no multi-statement
/// batch), seeds <see cref="RowCount"/> Shipper rows, and exposes the generated
/// <see cref="ShipperStore"/>, the connection string, and a non-pooled
/// <see cref="IDbContextFactory{OracleShipperContext}"/> for EF Core.
/// </summary>
/// <remarks>
/// Each benchmark class creates one of these in <c>[GlobalSetup]</c> and disposes it in
/// <c>[GlobalCleanup]</c>. The container is started once per parameter tier; EF uses a
/// non-pooled factory so it pays per-operation context construction — the same lifecycle
/// ADO, Dapper, and Inquiry each take (fresh connection per call).
/// </remarks>
public sealed class OracleBenchmarkDatabase : IAsyncDisposable
{
    private readonly OracleContainer _container;
    private readonly ServiceProvider _services;

    private OracleBenchmarkDatabase(
        OracleContainer container,
        string connectionString,
        ServiceProvider services,
        IDbContextFactory<OracleShipperContext> dbContextFactory,
        int rowCount)
    {
        _container        = container;
        _services         = services;
        ConnectionString  = connectionString;
        DbContextFactory  = dbContextFactory;
        RowCount          = rowCount;
    }

    public string ConnectionString { get; }

    /// <summary>Number of Shipper rows seeded into this database.</summary>
    public int RowCount { get; }

    public IDbContextFactory<OracleShipperContext> DbContextFactory { get; }

    public ShipperStore Shippers => _services.GetRequiredService<ShipperStore>();

    /// <summary>
    /// Seeds <paramref name="seedRows"/> Shipper rows. Returns the freshly created harness;
    /// callers must dispose it.
    /// </summary>
    public static async Task<OracleBenchmarkDatabase> CreateAsync(int seedRows)
    {
        // Use the XE image whose service name (XEPDB1) matches what Testcontainers.Oracle bakes
        // into its connection string. The oracle-free image serves FREEPDB1 instead, so its
        // generated connection string fails with ORA-12514 (service not known).
        var container = new OracleBuilder()
            .WithImage("gvenzl/oracle-xe:21-slim-faststart")
            .Build();
        await container.StartAsync().ConfigureAwait(false);

        // The container's default user is limited; rewrite to SYSTEM for DDL access.
        // In the gvenzl image the SYSTEM password equals the configured ORACLE_PASSWORD
        // (same password Testcontainers sets for the app user), so only the user id is swapped.
        var adminConnectionString = new OracleConnectionStringBuilder(container.GetConnectionString())
        {
            UserID = "SYSTEM",
        }.ToString();

        // The faststart image's listener accepts connections a moment before the database service
        // is registered; poll until the service is actually serving.
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

        var dbContextFactory = services.GetRequiredService<IDbContextFactory<OracleShipperContext>>();
        var harness = new OracleBenchmarkDatabase(container, adminConnectionString, services, dbContextFactory, seedRows);

        await harness.SeedAsync().ConfigureAwait(false);
        return harness;
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

    private async Task SeedAsync()
    {
        // Seed via raw SQL inside a single transaction — Inquiry / EF / Dapper inserts are
        // the subjects of the benchmark; we don't want their per-row cost to slow setup.
        await using var connection = new OracleConnection(ConnectionString);
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
            for (int i = 0; i < RowCount; i++)
            {
                pCompany.Value = $"Shipper {i}";
                pPhone.Value   = $"555-{i:0000}";
                await insert.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }

        await tx.CommitAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync().ConfigureAwait(false);
        // Force-close pooled connections before disposing the container.
        OracleConnection.ClearAllPools();
        await _container.DisposeAsync().ConfigureAwait(false);
    }
}
