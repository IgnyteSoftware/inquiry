using Inquiry.Benchmarks.PostgreSql.Ef;
using Inquiry.DependencyInjection;
using Inquiry.Northwind;
using Inquiry.Northwind.Stores;
using Inquiry.PostgreSql.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Inquiry.Benchmarks.PostgreSql;

/// <summary>
/// Per-benchmark-class scaffolding: provisions a PostgreSQL Testcontainer, applies
/// <see cref="NorthwindSchema.PostgreSqlDdl"/>, seeds <see cref="RowCount"/> Shipper rows,
/// and exposes the generated <see cref="ShipperStore"/>, the connection string, and a
/// non-pooled <see cref="IDbContextFactory{PgShipperContext}"/> for EF Core.
/// </summary>
/// <remarks>
/// Each benchmark class creates one of these in <c>[GlobalSetup]</c> and disposes it in
/// <c>[GlobalCleanup]</c>. The container is started once per parameter tier; EF uses a
/// non-pooled factory so it pays per-operation context construction — the same lifecycle
/// ADO, Dapper, and Inquiry each take (fresh connection per call).
/// </remarks>
public sealed class PostgreSqlBenchmarkDatabase : IAsyncDisposable
{
    private readonly PostgreSqlContainer _container;
    private readonly ServiceProvider _services;

    private PostgreSqlBenchmarkDatabase(
        PostgreSqlContainer container,
        string connectionString,
        ServiceProvider services,
        IDbContextFactory<PgShipperContext> dbContextFactory,
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

    public IDbContextFactory<PgShipperContext> DbContextFactory { get; }

    public ShipperStore Shippers => _services.GetRequiredService<ShipperStore>();

    /// <summary>
    /// Seeds <paramref name="seedRows"/> Shipper rows. Returns the freshly created harness;
    /// callers must dispose it.
    /// </summary>
    public static async Task<PostgreSqlBenchmarkDatabase> CreateAsync(int seedRows)
    {
        var container = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .Build();
        await container.StartAsync().ConfigureAwait(false);

        var connectionString = container.GetConnectionString();

        // Apply the Northwind DDL (full schema; idempotent).
        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync().ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = NorthwindSchema.PostgreSqlDdl;
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        var services = new ServiceCollection()
            .AddInquiry()
            .AddInquiryPostgreSql(connectionString)
            // Non-pooled: each CreateDbContext builds a fresh context, so EF pays per-operation
            // setup the same way ADO/Dapper/Inquiry each open a fresh connection per call.
            .AddDbContextFactory<PgShipperContext>(options => options.UseNpgsql(connectionString))
            .BuildServiceProvider();

        var dbContextFactory = services.GetRequiredService<IDbContextFactory<PgShipperContext>>();
        var harness = new PostgreSqlBenchmarkDatabase(container, connectionString, services, dbContextFactory, seedRows);

        await harness.SeedAsync().ConfigureAwait(false);
        return harness;
    }

    private async Task SeedAsync()
    {
        // Seed via raw SQL inside a single transaction — Inquiry / EF / Dapper inserts are
        // the subjects of the benchmark; we don't want their per-row cost to slow setup.
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var tx = await connection.BeginTransactionAsync().ConfigureAwait(false);

        // Shippers — SERIAL PK; Npgsql uses @name parameters.
        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = tx;
            insert.CommandText = "INSERT INTO \"Shippers\" (\"CompanyName\", \"Phone\") VALUES (@company, @phone);";
            var pCompany = insert.Parameters.Add("company", NpgsqlTypes.NpgsqlDbType.Text);
            var pPhone   = insert.Parameters.Add("phone",   NpgsqlTypes.NpgsqlDbType.Text);
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
