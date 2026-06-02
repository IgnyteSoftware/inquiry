using Inquiry;
using Inquiry.DependencyInjection;
using Inquiry.Benchmarks.Ef;
using Inquiry.Northwind;
using Inquiry.Northwind.Models;
using Inquiry.Northwind.Stores;
using Inquiry.Sqlite.DependencyInjection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Inquiry.Benchmarks;

/// <summary>
/// Per-benchmark-class scaffolding: creates a fresh SQLite file, runs the Northwind DDL,
/// seeds <see cref="RowCount"/> Customer / Product / Shipper rows, and exposes a configured
/// service provider for the Inquiry stores, a per-call (non-pooled) <see cref="NorthwindDbContext"/>
/// factory for EF Core, and connection strings the Dapper / ADO.NET benchmarks own
/// their own connections against.
/// </summary>
/// <remarks>
/// Each benchmark class creates one of these in <c>[GlobalSetup]</c> and disposes it in
/// <c>[GlobalCleanup]</c>; the database file is deleted on cleanup so re-runs do not
/// accumulate rows from earlier inserts. The seeded row count is parameterized so a class
/// can drive both the 1 000-row and 100 000-row tiers via a <c>[Params]</c> field.
/// </remarks>
public sealed class BenchmarkDatabase : IAsyncDisposable
{
    public const int DefaultSeedRows = 1000;

    private readonly string _databasePath;
    private readonly ServiceProvider _services;

    private BenchmarkDatabase(string databasePath, string connectionString, ServiceProvider services, IDbContextFactory<NorthwindDbContext> dbContextFactory, int seedRows)
    {
        _databasePath = databasePath;
        ConnectionString = connectionString;
        _services = services;
        DbContextFactory = dbContextFactory;
        RowCount = seedRows;
    }

    public string ConnectionString { get; }

    /// <summary>Number of Customer / Product / Shipper rows seeded into this database.</summary>
    public int RowCount { get; }

    public IDbContextFactory<NorthwindDbContext> DbContextFactory { get; }

    public IInquiry Inquiry => _services.GetRequiredService<IInquiry>();
    public CustomerStore Customers => _services.GetRequiredService<CustomerStore>();
    public ProductStore  Products  => _services.GetRequiredService<ProductStore>();
    public ShipperStore  Shippers  => _services.GetRequiredService<ShipperStore>();
    public RegionStore   Regions   => _services.GetRequiredService<RegionStore>();

    /// <summary>
    /// Seeds <paramref name="seedRows"/> rows of each benchmarked entity. Returns the freshly
    /// created harness; callers must dispose it.
    /// </summary>
    public static async Task<BenchmarkDatabase> CreateAsync(int seedRows = DefaultSeedRows)
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"inquiry_bench_{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={databasePath}";

        // Apply the shared Northwind DDL.
        await using (var connection = new SqliteConnection(connectionString))
        {
            await connection.OpenAsync().ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = NorthwindSchema.SqliteDdl;
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        var services = new ServiceCollection()
            .AddInquiry()
            .AddInquirySqlite(connectionString)
            // Non-pooled: each CreateDbContext builds a fresh context, so EF pays per-operation
            // setup the same way ADO/Dapper/Inquiry each open a fresh connection per call. A pooled
            // factory would let EF reuse warm context state the other three legs never get — an
            // unfair advantage that breaks the apples-to-apples comparison.
            .AddDbContextFactory<NorthwindDbContext>(options => options.UseSqlite(connectionString))
            .BuildServiceProvider();

        var dbContextFactory = services.GetRequiredService<IDbContextFactory<NorthwindDbContext>>();
        var harness = new BenchmarkDatabase(databasePath, connectionString, services, dbContextFactory, seedRows);

        await harness.SeedAsync().ConfigureAwait(false);
        return harness;
    }

    private async Task SeedAsync()
    {
        // Seed via raw SQL inside a single transaction — Inquiry / EF / Dapper inserts are
        // the subjects of the benchmark; we don't want their per-row cost to slow setup.
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var tx = (SqliteTransaction)await connection.BeginTransactionAsync().ConfigureAwait(false);

        // Customers — string PK; deterministic 5-char IDs from base-36 of an integer counter.
        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = tx;
            insert.CommandText =
                "INSERT INTO Customers (CustomerID, CompanyName, ContactName, Country, City) " +
                "VALUES ($id, $company, $contact, $country, $city);";
            var pId       = insert.Parameters.Add("$id",      SqliteType.Text);
            var pCompany  = insert.Parameters.Add("$company", SqliteType.Text);
            var pContact  = insert.Parameters.Add("$contact", SqliteType.Text);
            var pCountry  = insert.Parameters.Add("$country", SqliteType.Text);
            var pCity     = insert.Parameters.Add("$city",    SqliteType.Text);
            await insert.PrepareAsync().ConfigureAwait(false);
            for (int i = 0; i < RowCount; i++)
            {
                pId.Value      = SeedCustomerId(i);
                pCompany.Value = $"Company {i}";
                pContact.Value = $"Contact {i}";
                pCountry.Value = SeedCountries[i % SeedCountries.Length];
                pCity.Value    = $"City {i % 50}";
                await insert.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }

        // Shippers — IDENTITY PK; insert N rows.
        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = tx;
            insert.CommandText = "INSERT INTO Shippers (CompanyName, Phone) VALUES ($company, $phone);";
            var pCompany = insert.Parameters.Add("$company", SqliteType.Text);
            var pPhone   = insert.Parameters.Add("$phone",   SqliteType.Text);
            await insert.PrepareAsync().ConfigureAwait(false);
            for (int i = 0; i < RowCount; i++)
            {
                pCompany.Value = $"Shipper {i}";
                pPhone.Value   = $"555-{i:0000}";
                await insert.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }

        // Categories — Products has an FK; create a handful of categories so the FK column
        // has valid values. Capture the assigned CategoryIDs.
        var categoryIds = new List<long>();
        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = tx;
            insert.CommandText = "INSERT INTO Categories (CategoryName) VALUES ($name); SELECT last_insert_rowid();";
            var pName = insert.Parameters.Add("$name", SqliteType.Text);
            for (int i = 0; i < 8; i++)
            {
                pName.Value = $"Category {i}";
                categoryIds.Add((long)(await insert.ExecuteScalarAsync().ConfigureAwait(false))!);
            }
        }

        // Products — IDENTITY PK, references Categories.
        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = tx;
            insert.CommandText =
                "INSERT INTO Products (ProductName, CategoryID, UnitPrice, UnitsInStock, Discontinued) " +
                "VALUES ($name, $category, $price, $stock, 0);";
            var pName     = insert.Parameters.Add("$name",     SqliteType.Text);
            var pCategory = insert.Parameters.Add("$category", SqliteType.Integer);
            var pPrice    = insert.Parameters.Add("$price",    SqliteType.Real);
            var pStock    = insert.Parameters.Add("$stock",    SqliteType.Integer);
            await insert.PrepareAsync().ConfigureAwait(false);
            for (int i = 0; i < RowCount; i++)
            {
                pName.Value     = $"Product {i}";
                pCategory.Value = categoryIds[i % categoryIds.Count];
                pPrice.Value    = 10.0 + (i % 50);
                pStock.Value    = i % 100;
                await insert.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }

        await tx.CommitAsync().ConfigureAwait(false);
    }

    /// <summary>5-character zero-padded base-36 encoding of <paramref name="i"/>.</summary>
    public static string SeedCustomerId(int i)
    {
        const string alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        Span<char> buffer = stackalloc char[5];
        for (int p = 4; p >= 0; p--)
        {
            buffer[p] = alphabet[i % 36];
            i /= 36;
        }
        return new string(buffer);
    }

    public static readonly string[] SeedCountries =
    {
        "USA", "UK", "Germany", "France", "Italy", "Spain", "Brazil", "Canada", "Mexico", "Japan",
    };

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync().ConfigureAwait(false);

        // SQLite caches connections in a pool; clear them so the file is unlocked before delete.
        SqliteConnection.ClearAllPools();

        try
        {
            if (File.Exists(_databasePath))
            {
                File.Delete(_databasePath);
            }
        }
        catch
        {
            // Best-effort. If a benchmark process holds the file open, leave the temp file
            // and let the OS reclaim it.
        }
    }
}
