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
    /// <summary>
    /// Connection string to a database the test process can use to <c>CREATE DATABASE</c>
    /// (typically <c>postgres</c>). When unset, <see cref="PostgreSqlFactAttribute"/> skips
    /// the test rather than failing it.
    /// </summary>
    public const string ConnectionStringEnvironmentVariable = "INQUIRY_POSTGRESQL_CONNECTION_STRING";

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

    public static async Task<PostgreSqlTestHarness> CreateAsync(string? namePrefix = null)
    {
        var adminConnectionString = Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable)
            ?? throw new InvalidOperationException(
                $"Environment variable {ConnectionStringEnvironmentVariable} is not set; PostgreSqlFactAttribute should have skipped this test.");

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
            cmd.CommandText = NorthwindSchema.PostgreSqlDdl;
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
