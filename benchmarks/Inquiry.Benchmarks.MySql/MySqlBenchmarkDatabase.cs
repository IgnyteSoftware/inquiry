using Inquiry.Benchmarks.MySql.Ef;
using Inquiry.DependencyInjection;
using Inquiry.MySql.DependencyInjection;
using Inquiry.Northwind;
using Inquiry.Northwind.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using Testcontainers.MySql;

namespace Inquiry.Benchmarks.MySql;

/// <summary>
/// Per-benchmark-class scaffolding: provisions a MySQL Testcontainer, applies
/// <see cref="NorthwindSchema.MySqlDdl"/>, seeds <see cref="RowCount"/> Shipper rows,
/// and exposes the generated <see cref="ShipperStore"/>, the connection string, and a
/// non-pooled <see cref="IDbContextFactory{MySqlShipperContext}"/> for EF Core.
/// </summary>
/// <remarks>
/// Each benchmark class creates one of these in <c>[GlobalSetup]</c> and disposes it in
/// <c>[GlobalCleanup]</c>. The container is started once per parameter tier; EF uses a
/// non-pooled factory so it pays per-operation context construction — the same lifecycle
/// ADO, Dapper, and Inquiry each take (fresh connection per call).
/// </remarks>
public sealed class MySqlBenchmarkDatabase : IAsyncDisposable
{
    private readonly MySqlContainer _container;
    private readonly ServiceProvider _services;

    private MySqlBenchmarkDatabase(
        MySqlContainer container,
        string connectionString,
        ServiceProvider services,
        IDbContextFactory<MySqlShipperContext> dbContextFactory,
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

    public IDbContextFactory<MySqlShipperContext> DbContextFactory { get; }

    public ShipperStore Shippers => _services.GetRequiredService<ShipperStore>();

    /// <summary>
    /// Seeds <paramref name="seedRows"/> Shipper rows. Returns the freshly created harness;
    /// callers must dispose it.
    /// </summary>
    public static async Task<MySqlBenchmarkDatabase> CreateAsync(int seedRows)
    {
        var container = new MySqlBuilder()
            .WithImage("mysql:8.0")
            .Build();
        await container.StartAsync().ConfigureAwait(false);

        var connectionString = container.GetConnectionString();

        // Apply the Northwind DDL (full schema; idempotent).
        // MySqlDdl contains multiple statements; execute them one at a time.
        await using (var connection = new MySqlConnection(connectionString))
        {
            await connection.OpenAsync().ConfigureAwait(false);
            // Split on semicolons at end-of-line so each statement is sent individually.
            var statements = NorthwindSchema.MySqlDdl
                .Split(';')
                .Select(s => s.Trim())
                .Where(s => s.Length > 0);
            foreach (var stmt in statements)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = stmt;
                await command.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }

        var services = new ServiceCollection()
            .AddInquiry()
            .AddInquiryMySql(connectionString)
            // Non-pooled: each CreateDbContext builds a fresh context, so EF pays per-operation
            // setup the same way ADO/Dapper/Inquiry each open a fresh connection per call.
            .AddDbContextFactory<MySqlShipperContext>(options =>
                options.UseMySql(connectionString, new MySqlServerVersion(new Version(8, 0, 0))))
            .BuildServiceProvider();

        var dbContextFactory = services.GetRequiredService<IDbContextFactory<MySqlShipperContext>>();
        var harness = new MySqlBenchmarkDatabase(container, connectionString, services, dbContextFactory, seedRows);

        await harness.SeedAsync().ConfigureAwait(false);
        return harness;
    }

    private async Task SeedAsync()
    {
        // Seed via raw SQL inside a single transaction — Inquiry / EF / Dapper inserts are
        // the subjects of the benchmark; we don't want their per-row cost to slow setup.
        await using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var tx = await connection.BeginTransactionAsync().ConfigureAwait(false);

        // Shippers — AUTO_INCREMENT PK; MySqlConnector uses @name parameters.
        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = tx;
            insert.CommandText = "INSERT INTO `Shippers` (`CompanyName`, `Phone`) VALUES (@company, @phone);";
            var pCompany = insert.Parameters.AddWithValue("company", "");
            var pPhone   = insert.Parameters.AddWithValue("phone",   "");
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
        await _container.DisposeAsync().ConfigureAwait(false);
    }
}
