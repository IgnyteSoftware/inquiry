using Inquiry.DependencyInjection;
using Inquiry.FeatureCatalog;
using Inquiry.MariaDb.DependencyInjection;
using Inquiry.Northwind;
using Inquiry.Northwind.Stores;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;

namespace Inquiry.MariaDb.Tests.Fixtures;

/// <summary>
/// Creates a throwaway MariaDB database, runs the Northwind DDL against it, and exposes a
/// configured <see cref="ServiceProvider"/>. The database is dropped on disposal so parallel test
/// classes never collide on table state.
/// </summary>
internal sealed class MariaDbTestHarness : IAsyncDisposable
{
    private readonly string _adminConnectionString;
    private readonly string _databaseName;

    private MariaDbTestHarness(string adminConnectionString, string databaseName, string connectionString, ServiceProvider services)
    {
        _adminConnectionString = adminConnectionString;
        _databaseName = databaseName;
        ConnectionString = connectionString;
        Services = services;
    }

    public string ConnectionString { get; }

    public ServiceProvider Services { get; }

    public T GetRequiredService<T>() where T : notnull => Services.GetRequiredService<T>();

    public static Task<MariaDbTestHarness> CreateAsync(string adminConnectionString, string? namePrefix = null)
        => CreateFromDdlAsync(adminConnectionString, NorthwindSchema.MySqlDdl, namePrefix);

    public static async Task<MariaDbTestHarness> CreateFromDdlAsync(
        string adminConnectionString,
        string ddl,
        string? namePrefix = null,
        Action<string>? databaseCreated = null)
    {
        var prefix = (namePrefix ?? "inquiry").ToLowerInvariant();
        var databaseName = prefix + "_" + Guid.NewGuid().ToString("N");
        var wasCreated = false;

        try
        {
            await using (var admin = new MySqlConnection(adminConnectionString))
            {
                await admin.OpenAsync();
                await using var cmd = admin.CreateCommand();
                cmd.CommandText = $"CREATE DATABASE {QuoteIdentifier(databaseName)};";
                await cmd.ExecuteNonQueryAsync();
                wasCreated = true;
                databaseCreated?.Invoke(databaseName);
            }

            var connectionString = new MySqlConnectionStringBuilder(adminConnectionString)
            {
                Database = databaseName,
            }.ToString();

            await using (var db = new MySqlConnection(connectionString))
            {
                await db.OpenAsync();
                await using var cmd = db.CreateCommand();
                cmd.CommandText = ddl;
                await cmd.ExecuteNonQueryAsync();
            }

            var services = new ServiceCollection()
                .AddInquiry(typeof(CustomerStore).Assembly, typeof(GeneratedItemStore).Assembly, typeof(VersionedItemStore).Assembly)
                .AddInquiryMariaDb(connectionString)
                .BuildServiceProvider();

            return new MariaDbTestHarness(adminConnectionString, databaseName, connectionString, services);
        }
        catch (Exception setupException) when (wasCreated)
        {
            MySqlConnection.ClearAllPools();
            try
            {
                await DropDatabaseAsync(adminConnectionString, databaseName);
            }
            catch (Exception cleanupException)
            {
                throw new AggregateException(
                    $"MariaDB test database '{databaseName}' setup and cleanup both failed.",
                    setupException,
                    cleanupException);
            }

            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();

        // Force-close pooled connections so DROP DATABASE doesn't fail with "database in use".
        MySqlConnection.ClearAllPools();

        try
        {
            await DropDatabaseAsync(_adminConnectionString, _databaseName);
        }
        catch
        {
            // Best-effort cleanup. Don't fail the test on teardown.
        }
    }

    private static async Task DropDatabaseAsync(string adminConnectionString, string databaseName)
    {
        await using var admin = new MySqlConnection(adminConnectionString);
        await admin.OpenAsync();
        await using var cmd = admin.CreateCommand();
        cmd.CommandText = $"DROP DATABASE IF EXISTS {QuoteIdentifier(databaseName)};";
        await cmd.ExecuteNonQueryAsync();
    }

    private static string QuoteIdentifier(string identifier)
        => "`" + identifier.Replace("`", "``", StringComparison.Ordinal) + "`";
}
