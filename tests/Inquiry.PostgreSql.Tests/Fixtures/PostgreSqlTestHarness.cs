using Inquiry.DependencyInjection;
using Inquiry.Northwind;
using Inquiry.PostgreSql.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Inquiry.PostgreSql.Tests.Fixtures;

/// <summary>
/// Creates a throwaway PostgreSQL database, runs the Northwind DDL against it, and exposes
/// a configured <see cref="ServiceProvider"/>. The database is dropped on disposal so
/// parallel test classes never collide on table state.
/// </summary>
internal sealed class PostgreSqlTestHarness : IAsyncDisposable
{
    private readonly string _adminConnectionString;
    private readonly string _databaseName;

    private PostgreSqlTestHarness(string adminConnectionString, string databaseName, string connectionString, ServiceProvider services)
    {
        _adminConnectionString = adminConnectionString;
        _databaseName = databaseName;
        ConnectionString = connectionString;
        Services = services;
    }

    public string ConnectionString { get; }

    public ServiceProvider Services { get; }

    public T GetRequiredService<T>() where T : notnull => Services.GetRequiredService<T>();

    public static Task<PostgreSqlTestHarness> CreateAsync(string adminConnectionString, string? namePrefix = null)
        => CreateFromDdlAsync(adminConnectionString, NorthwindSchema.PostgreSqlDdl, namePrefix);

    public static async Task<PostgreSqlTestHarness> CreateFromDdlAsync(string adminConnectionString, string ddl, string? namePrefix = null)
    {
        var prefix = (namePrefix ?? "inquiry").ToLowerInvariant();
        var databaseName = prefix + "_" + Guid.NewGuid().ToString("N");

        await using (var admin = new NpgsqlConnection(adminConnectionString))
        {
            await admin.OpenAsync();
            await using var cmd = admin.CreateCommand();
            // Identifier quoted so PostgreSQL preserves casing exactly; PostgreSQL identifiers
            // are otherwise folded to lowercase.
            cmd.CommandText = $"CREATE DATABASE \"{databaseName}\";";
            await cmd.ExecuteNonQueryAsync();
        }

        var connectionString = new NpgsqlConnectionStringBuilder(adminConnectionString)
        {
            Database = databaseName,
        }.ToString();

        await using (var db = new NpgsqlConnection(connectionString))
        {
            await db.OpenAsync();
            await using var cmd = db.CreateCommand();
            cmd.CommandText = ddl;
            await cmd.ExecuteNonQueryAsync();
        }

        var services = new ServiceCollection()
            .AddInquiry()
            .AddInquiryPostgreSql(connectionString)
            .BuildServiceProvider();

        return new PostgreSqlTestHarness(adminConnectionString, databaseName, connectionString, services);
    }

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();

        // Force-close pooled connections so DROP DATABASE doesn't fail with "database in use".
        NpgsqlConnection.ClearAllPools();

        try
        {
            await using var admin = new NpgsqlConnection(_adminConnectionString);
            await admin.OpenAsync();
            await using var cmd = admin.CreateCommand();
            cmd.CommandText = $"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE);";
            await cmd.ExecuteNonQueryAsync();
        }
        catch
        {
            // Best-effort cleanup. Don't fail the test on teardown.
        }
    }
}
