using Inquiry.Benchmarks.MariaDb.Ef;
using Inquiry.DependencyInjection;
using Inquiry.MariaDb.DependencyInjection;
using Inquiry.Northwind;
using Inquiry.Northwind.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using Testcontainers.MariaDb;

namespace Inquiry.Benchmarks.MariaDb;

public sealed class MariaDbBenchmarkDatabase : IAsyncDisposable
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static MariaDbContainer? _container;
    private static ServiceProvider? _services;
    private static string? _connectionString;
    private static IDbContextFactory<MariaDbShipperContext>? _dbContextFactory;

    private MariaDbBenchmarkDatabase(int rowCount) => RowCount = rowCount;

    public string ConnectionString => _connectionString!;

    public int RowCount { get; }

    public IDbContextFactory<MariaDbShipperContext> DbContextFactory => _dbContextFactory!;

    public ShipperStore Shippers => _services!.GetRequiredService<ShipperStore>();

    public static async Task<MariaDbBenchmarkDatabase> CreateAsync(int seedRows)
    {
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_container is null)
            {
                var container = new MariaDbBuilder("mariadb:11.4").Build();
                await container.StartAsync().ConfigureAwait(false);
                var connectionString = container.GetConnectionString();

                await using (var connection = new MySqlConnection(connectionString))
                {
                    await connection.OpenAsync().ConfigureAwait(false);
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
                    .AddInquiryMariaDb(connectionString)
                    .AddDbContextFactory<MariaDbShipperContext>(options =>
                        options.UseMySql(connectionString, new MariaDbServerVersion(new Version(11, 4, 0))))
                    .BuildServiceProvider();

                await SeedAsync(connectionString, seedRows).ConfigureAwait(false);

                _connectionString = connectionString;
                _services = services;
                _dbContextFactory = services.GetRequiredService<IDbContextFactory<MariaDbShipperContext>>();
                _container = container;

                AppDomain.CurrentDomain.ProcessExit += static (_, _) =>
                {
                    try
                    {
                        _services?.Dispose();
                        _container?.DisposeAsync().AsTask().GetAwaiter().GetResult();
                    }
                    catch { }
                };
            }
        }
        finally
        {
            Gate.Release();
        }

        return new MariaDbBenchmarkDatabase(seedRows);
    }

    private static async Task SeedAsync(string connectionString, int rowCount)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var tx = await connection.BeginTransactionAsync().ConfigureAwait(false);

        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = tx;
            insert.CommandText = "INSERT INTO `Shippers` (`CompanyName`, `Phone`) VALUES (@company, @phone);";
            var pCompany = insert.Parameters.AddWithValue("company", "");
            var pPhone   = insert.Parameters.AddWithValue("phone",   "");
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

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
