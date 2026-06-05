using Inquiry.DependencyInjection;
using Inquiry.FeatureCatalog;
using Inquiry.MySql.DependencyInjection;
using Inquiry.Northwind;
using Inquiry.Northwind.Stores;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;

namespace Inquiry.MySql.Tests.Fixtures;

/// <summary>
/// Creates a throwaway MySQL database, runs the Northwind DDL against it, and exposes a
/// configured <see cref="ServiceProvider"/>. The database is dropped on disposal so parallel test
/// classes never collide on table state.
/// </summary>
internal sealed class MySqlTestHarness : IAsyncDisposable
{
    private readonly string _adminConnectionString;
    private readonly string _databaseName;

    private MySqlTestHarness(string adminConnectionString, string databaseName, string connectionString, ServiceProvider services)
    {
        _adminConnectionString = adminConnectionString;
        _databaseName = databaseName;
        ConnectionString = connectionString;
        Services = services;
    }

    public string ConnectionString { get; }

    public ServiceProvider Services { get; }

    public T GetRequiredService<T>() where T : notnull => Services.GetRequiredService<T>();

    public static Task<MySqlTestHarness> CreateAsync(string adminConnectionString, string? namePrefix = null)
        => CreateFromDdlAsync(adminConnectionString, NorthwindSchema.MySqlDdl, namePrefix);

    public static async Task<MySqlTestHarness> CreateFromDdlAsync(string adminConnectionString, string ddl, string? namePrefix = null)
    {
        var prefix = (namePrefix ?? "inquiry").ToLowerInvariant();
        var databaseName = prefix + "_" + Guid.NewGuid().ToString("N");

        await using (var admin = new MySqlConnection(adminConnectionString))
        {
            await admin.OpenAsync();
            await using var cmd = admin.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE `{databaseName}`;";
            await cmd.ExecuteNonQueryAsync();
        }

        var connectionString = new MySqlConnectionStringBuilder(adminConnectionString)
        {
            Database = databaseName,
            // Keep MySqlConnector's multi-statement support on for the emulated
            // INSERT ...; SELECT returning batches.
            AllowUserVariables = true,
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
            .AddInquiryMySql(connectionString)
            .BuildServiceProvider();

        return new MySqlTestHarness(adminConnectionString, databaseName, connectionString, services);
    }

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();

        // Force-close pooled connections so DROP DATABASE doesn't fail with "database in use".
        MySqlConnection.ClearAllPools();

        try
        {
            await using var admin = new MySqlConnection(_adminConnectionString);
            await admin.OpenAsync();
            await using var cmd = admin.CreateCommand();
            cmd.CommandText = $"DROP DATABASE IF EXISTS `{_databaseName}`;";
            await cmd.ExecuteNonQueryAsync();
        }
        catch
        {
            // Best-effort cleanup. Don't fail the test on teardown.
        }
    }
}
