using Inquiry.Benchmarks.SqlServer.Ef;
using Inquiry.DependencyInjection;
using Inquiry.Northwind;
using Inquiry.Northwind.Models;
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
    private static IDbContextFactory<SqlServerProductContext>? _productContextFactory;

    private SqlServerBenchmarkDatabase(int rowCount) => RowCount = rowCount;

    public string ConnectionString => _connectionString!;

    /// <summary>Number of Shipper rows seeded into the shared database.</summary>
    public int RowCount { get; }

    public IDbContextFactory<SqlServerShipperContext> DbContextFactory => _dbContextFactory!;
    public IDbContextFactory<SqlServerProductContext> ProductContextFactory => _productContextFactory!;

    public ShipperStore Shippers => _services!.GetRequiredService<ShipperStore>();
    public ProductStore Products => _services!.GetRequiredService<ProductStore>();
    public CategoryStore Categories => _services!.GetRequiredService<CategoryStore>();
    public BenchmarkM2MOrderStore ManyToManyOrders => _services!.GetRequiredService<BenchmarkM2MOrderStore>();

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
                var container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
                    .Build();
                await container.StartAsync().ConfigureAwait(false);
                var connectionString = container.GetConnectionString();

                await using (var connection = new SqlConnection(connectionString))
                {
                    await connection.OpenAsync().ConfigureAwait(false);
                    await using var command = connection.CreateCommand();
                    command.CommandText = NorthwindSchema.SqlServerDdl + """
                        CREATE TABLE BenchmarkM2MOrder (Id BIGINT IDENTITY(1,1) PRIMARY KEY, Name NVARCHAR(200) NOT NULL);
                        CREATE TABLE BenchmarkM2MProduct (Id BIGINT IDENTITY(1,1) PRIMARY KEY, Title NVARCHAR(200) NOT NULL);
                        CREATE TABLE BenchmarkM2MOrderProduct (OrderId BIGINT NOT NULL, ProductId BIGINT NOT NULL, PRIMARY KEY (OrderId, ProductId));
                        """;
                    await command.ExecuteNonQueryAsync().ConfigureAwait(false);
                }

                var services = new ServiceCollection()
                    .AddInquiry()
                    .AddInquiryGeneratedStores()
                    .AddInquirySqlServer(connectionString)
                    // Non-pooled: each CreateDbContext builds a fresh context, so EF pays per-operation
                    // setup the same way ADO/Dapper/Inquiry each open a fresh connection per call.
                    .AddDbContextFactory<SqlServerShipperContext>(options => options.UseSqlServer(connectionString))
                    .AddDbContextFactory<SqlServerProductContext>(options => options.UseSqlServer(connectionString))
                    .BuildServiceProvider();

                await SeedAsync(connectionString, seedRows).ConfigureAwait(false);

                // DLG: create its stored procedures and write the .config it self-loads.
                await Dlg.DlgSetup.ApplyStoredProceduresAsync(connectionString).ConfigureAwait(false);
                Dlg.DlgSetup.PrimeConfig(connectionString);

                _connectionString = connectionString;
                _services = services;
                _dbContextFactory = services.GetRequiredService<IDbContextFactory<SqlServerShipperContext>>();
                _productContextFactory = services.GetRequiredService<IDbContextFactory<SqlServerProductContext>>();
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

        // Shippers — IDENTITY PK; ShipperID is not supplied.
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

        // Categories — 10 fixed categories; capture their IDENTITY ids for product FKs.
        var categoryIds = new List<int>();
        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = (SqlTransaction)tx;
            insert.CommandText =
                "INSERT INTO Categories (CategoryName, Description) OUTPUT inserted.CategoryID VALUES (@name, @desc);";
            var pName = insert.Parameters.Add("@name", System.Data.SqlDbType.NVarChar, 15);
            var pDesc = insert.Parameters.Add("@desc", System.Data.SqlDbType.NVarChar, -1);
            for (int i = 0; i < 10; i++)
            {
                pName.Value = $"Category {i}";
                pDesc.Value = $"Description {i}";
                categoryIds.Add((int)(await insert.ExecuteScalarAsync().ConfigureAwait(false))!);
            }
        }

        // Products — rowCount rows spread across the categories; distinct names for LIKE search.
        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = (SqlTransaction)tx;
            insert.CommandText =
                "INSERT INTO Products (ProductName, CategoryID, UnitPrice, Discontinued) " +
                "VALUES (@name, @cat, @price, 0);";
            var pName  = insert.Parameters.Add("@name",  System.Data.SqlDbType.NVarChar, 40);
            var pCat   = insert.Parameters.Add("@cat",   System.Data.SqlDbType.Int);
            var pPrice = insert.Parameters.Add("@price", System.Data.SqlDbType.Decimal);
            for (int i = 0; i < rowCount; i++)
            {
                pName.Value  = $"Product {i}";
                pCat.Value   = categoryIds[i % categoryIds.Count];
                pPrice.Value = 10m + (i % 100);
                await insert.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }

        // M:N eager evidence: two eligible parents, eight participating children, and rowCount
        // unrelated children. The old-shape control therefore materializes rowCount + 8 children;
        // Inquiry's generated child-key IN query materializes only the eight participating rows.
        var orderIds = new List<long>();
        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = (SqlTransaction)tx;
            insert.CommandText =
                "INSERT INTO BenchmarkM2MOrder (Name) OUTPUT inserted.Id VALUES (@name);";
            var pName = insert.Parameters.Add("@name", System.Data.SqlDbType.NVarChar, 200);
            for (var i = 0; i < 2; i++)
            {
                pName.Value = "Order " + i;
                orderIds.Add((long)(await insert.ExecuteScalarAsync().ConfigureAwait(false))!);
            }
        }

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = (SqlTransaction)tx;
            command.CommandText = """
                INSERT INTO BenchmarkM2MProduct (Title) OUTPUT inserted.Id VALUES (@title);
                """;
            var pTitle = command.Parameters.Add("@title", System.Data.SqlDbType.NVarChar, 200);
            var participatingIds = new List<long>();
            for (var i = 0; i < rowCount + 8; i++)
            {
                pTitle.Value = i < 8 ? "Participating " + i : "Unrelated " + (i - 8);
                var id = (long)(await command.ExecuteScalarAsync().ConfigureAwait(false))!;
                if (i < 8) participatingIds.Add(id);
            }

            command.CommandText =
                "INSERT INTO BenchmarkM2MOrderProduct (OrderId, ProductId) VALUES (@orderId, @productId);";
            command.Parameters.Clear();
            var pOrderId = command.Parameters.Add("@orderId", System.Data.SqlDbType.BigInt);
            var pProductId = command.Parameters.Add("@productId", System.Data.SqlDbType.BigInt);
            for (var i = 0; i < participatingIds.Count; i++)
            {
                pOrderId.Value = orderIds[i % orderIds.Count];
                pProductId.Value = participatingIds[i];
                await command.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }

        await tx.CommitAsync().ConfigureAwait(false);
    }

    /// <summary>No-op: the shared container outlives individual benchmark methods (see class remarks).</summary>
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
