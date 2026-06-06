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
/// Process-wide PostgreSQL Testcontainer + DI for the benchmark suite. The container is the
/// expensive resource, so it is started <b>once per process</b> and reused by every benchmark
/// method (BenchmarkDotNet must run <c>--inProcess</c> for that sharing to take effect); the seed
/// runs once. Read benchmarks are non-mutating, and the write benchmarks run after them (declared
/// order, see <c>[Orderer]</c> on the benchmark class) and target a stable key, so a per-method
/// reseed is unnecessary. The container is torn down at process exit (and by the Testcontainers
/// resource reaper as a backstop). EF uses a non-pooled factory so it pays per-operation context
/// construction — the same lifecycle ADO, Dapper, and Inquiry each take (fresh connection per call).
/// </summary>
public sealed class PostgreSqlBenchmarkDatabase : IAsyncDisposable
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static PostgreSqlContainer? _container;
    private static ServiceProvider? _servicesWithoutPreparation;
    private static ServiceProvider? _servicesWithPreparation;
    private static string? _connectionString;
    private static IDbContextFactory<PgShipperContext>? _dbContextFactory;

    private PostgreSqlBenchmarkDatabase(int rowCount) => RowCount = rowCount;

    public string ConnectionString => _connectionString!;

    /// <summary>Number of benchmark rows seeded into the shared database.</summary>
    public int RowCount { get; }

    public IDbContextFactory<PgShipperContext> DbContextFactory => _dbContextFactory!;

    public ShipperStore Shippers => GetRequiredService<ShipperStore>(PreparedStatementMode.None);

    public T GetRequiredService<T>(PreparedStatementMode mode)
        where T : notnull
    {
        var services = mode == PreparedStatementMode.Auto
            ? _servicesWithPreparation
            : _servicesWithoutPreparation;

        return services!.GetRequiredService<T>();
    }

    /// <summary>
    /// Returns a handle over the process-wide shared container, starting + seeding it on first call.
    /// </summary>
    public static async Task<PostgreSqlBenchmarkDatabase> CreateAsync(int seedRows)
    {
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_container is null)
            {
                var container = new PostgreSqlBuilder("postgres:16-alpine").Build();
                await container.StartAsync().ConfigureAwait(false);
                var connectionString = container.GetConnectionString();

                await using (var connection = new NpgsqlConnection(connectionString))
                {
                    await connection.OpenAsync().ConfigureAwait(false);
                    await using var command = connection.CreateCommand();
                    command.CommandText = NorthwindSchema.PostgreSqlDdl;
                    await command.ExecuteNonQueryAsync().ConfigureAwait(false);
                }

                var connectionStringWithoutPreparation = WithApplicationName(connectionString, "InquiryBenchPreparedNone");
                var connectionStringWithPreparation = WithApplicationName(connectionString, "InquiryBenchPreparedAuto");

                var servicesWithoutPreparation = new ServiceCollection()
                    .AddInquiry(options => options.PrepareStatements = PreparedStatementMode.None, typeof(ShipperStore).Assembly)
                    .AddInquiryPostgreSql(connectionStringWithoutPreparation)
                    // Non-pooled: each CreateDbContext builds a fresh context, so EF pays per-operation
                    // setup the same way ADO/Dapper/Inquiry each open a fresh connection per call.
                    .AddDbContextFactory<PgShipperContext>(options => options.UseNpgsql(connectionStringWithoutPreparation))
                    .BuildServiceProvider();

                var servicesWithPreparation = new ServiceCollection()
                    .AddInquiry(options => options.PrepareStatements = PreparedStatementMode.Auto, typeof(ShipperStore).Assembly)
                    .AddInquiryPostgreSql(connectionStringWithPreparation)
                    .BuildServiceProvider();

                await SeedAsync(connectionString, seedRows).ConfigureAwait(false);

                _connectionString = connectionString;
                _servicesWithoutPreparation = servicesWithoutPreparation;
                _servicesWithPreparation = servicesWithPreparation;
                _dbContextFactory = servicesWithoutPreparation.GetRequiredService<IDbContextFactory<PgShipperContext>>();
                _container = container;

                AppDomain.CurrentDomain.ProcessExit += static (_, _) =>
                {
                    try
                    {
                        _servicesWithPreparation?.Dispose();
                        _servicesWithoutPreparation?.Dispose();
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

        return new PostgreSqlBenchmarkDatabase(seedRows);
    }

    private static string WithApplicationName(string connectionString, string applicationName)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            ApplicationName = applicationName,
        };

        return builder.ToString();
    }

    private static async Task SeedAsync(string connectionString, int rowCount)
    {
        await using var connection = new NpgsqlConnection(connectionString);
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
            for (int i = 0; i < rowCount; i++)
            {
                pCompany.Value = $"Shipper {i}";
                pPhone.Value   = $"555-{i:0000}";
                await insert.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }

        var categoryIds = await SeedCategoriesAsync(connection, tx).ConfigureAwait(false);
        var supplierIds = await SeedSuppliersAsync(connection, tx).ConfigureAwait(false);
        await SeedProductsAsync(connection, tx, rowCount, categoryIds, supplierIds).ConfigureAwait(false);

        await tx.CommitAsync().ConfigureAwait(false);
    }

    private static async Task<int[]> SeedCategoriesAsync(NpgsqlConnection connection, NpgsqlTransaction tx)
    {
        var ids = new int[10];
        await using var insert = connection.CreateCommand();
        insert.Transaction = tx;
        insert.CommandText =
            "INSERT INTO \"Categories\" (\"CategoryName\", \"Description\") " +
            "VALUES (@name, @description) RETURNING \"CategoryID\";";
        var pName = insert.Parameters.Add("name", NpgsqlTypes.NpgsqlDbType.Text);
        var pDescription = insert.Parameters.Add("description", NpgsqlTypes.NpgsqlDbType.Text);
        await insert.PrepareAsync().ConfigureAwait(false);

        for (var i = 0; i < ids.Length; i++)
        {
            pName.Value = $"Category {i}";
            pDescription.Value = $"Benchmark category {i}";
            ids[i] = (int)(await insert.ExecuteScalarAsync().ConfigureAwait(false))!;
        }

        return ids;
    }

    private static async Task<int[]> SeedSuppliersAsync(NpgsqlConnection connection, NpgsqlTransaction tx)
    {
        var ids = new int[10];
        await using var insert = connection.CreateCommand();
        insert.Transaction = tx;
        insert.CommandText =
            "INSERT INTO \"Suppliers\" (\"CompanyName\", \"Phone\") " +
            "VALUES (@company, @phone) RETURNING \"SupplierID\";";
        var pCompany = insert.Parameters.Add("company", NpgsqlTypes.NpgsqlDbType.Text);
        var pPhone = insert.Parameters.Add("phone", NpgsqlTypes.NpgsqlDbType.Text);
        await insert.PrepareAsync().ConfigureAwait(false);

        for (var i = 0; i < ids.Length; i++)
        {
            pCompany.Value = $"Supplier {i}";
            pPhone.Value = $"555-S{i:000}";
            ids[i] = (int)(await insert.ExecuteScalarAsync().ConfigureAwait(false))!;
        }

        return ids;
    }

    private static async Task SeedProductsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction tx,
        int rowCount,
        IReadOnlyList<int> categoryIds,
        IReadOnlyList<int> supplierIds)
    {
        await using var insert = connection.CreateCommand();
        insert.Transaction = tx;
        insert.CommandText =
            "INSERT INTO \"Products\" (\"ProductName\", \"SupplierID\", \"CategoryID\", \"QuantityPerUnit\", \"UnitPrice\", \"UnitsInStock\", \"UnitsOnOrder\", \"ReorderLevel\", \"Discontinued\") " +
            "VALUES (@name, @supplier, @category, @quantity, @price, @stock, @onOrder, @reorder, @discontinued);";
        var pName = insert.Parameters.Add("name", NpgsqlTypes.NpgsqlDbType.Text);
        var pSupplier = insert.Parameters.Add("supplier", NpgsqlTypes.NpgsqlDbType.Integer);
        var pCategory = insert.Parameters.Add("category", NpgsqlTypes.NpgsqlDbType.Integer);
        var pQuantity = insert.Parameters.Add("quantity", NpgsqlTypes.NpgsqlDbType.Text);
        var pPrice = insert.Parameters.Add("price", NpgsqlTypes.NpgsqlDbType.Numeric);
        var pStock = insert.Parameters.Add("stock", NpgsqlTypes.NpgsqlDbType.Smallint);
        var pOnOrder = insert.Parameters.Add("onOrder", NpgsqlTypes.NpgsqlDbType.Smallint);
        var pReorder = insert.Parameters.Add("reorder", NpgsqlTypes.NpgsqlDbType.Smallint);
        var pDiscontinued = insert.Parameters.Add("discontinued", NpgsqlTypes.NpgsqlDbType.Boolean);
        await insert.PrepareAsync().ConfigureAwait(false);

        for (var i = 0; i < rowCount; i++)
        {
            pName.Value = $"Product {i}";
            pSupplier.Value = supplierIds[i % supplierIds.Count];
            pCategory.Value = categoryIds[i % categoryIds.Count];
            pQuantity.Value = "12 boxes";
            pPrice.Value = 10m + (i % 100);
            pStock.Value = (short)(i % 100);
            pOnOrder.Value = (short)(i % 25);
            pReorder.Value = (short)(i % 10);
            pDiscontinued.Value = false;
            await insert.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
    }

    /// <summary>No-op: the shared container outlives individual benchmark methods (see class remarks).</summary>
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
