using Inquiry.DependencyInjection;
using Inquiry.MySql.DependencyInjection;
using Inquiry.Northwind;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;

namespace Inquiry.MySql.Tests.Fixtures;

/// <summary>
/// Creates a throwaway MySQL/MariaDB database, runs the Northwind DDL against it, and exposes a
/// configured <see cref="ServiceProvider"/>. The database is dropped on disposal so parallel test
/// classes never collide on table state.
/// </summary>
/// <remarks>
/// The shared <c>Inquiry.Northwind</c> stores bake their SQL against the SQLite dialect, which quotes
/// identifiers with double quotes. MySQL only treats <c>"..."</c> as an identifier (rather than a
/// string literal) under <c>ANSI_QUOTES</c>, so the harness appends that to the session
/// <c>sql_mode</c> via the connection string. SQL Server tolerates the same SQLite-dialect SQL
/// natively because its default <c>QUOTED_IDENTIFIER</c> mode is on.
/// </remarks>
internal sealed class MySqlTestHarness : IAsyncDisposable
{
    /// <summary>
    /// Connection string to a database the test process can use to <c>CREATE DATABASE</c>. When
    /// unset, <see cref="MySqlFactAttribute"/> skips the test rather than failing it.
    /// </summary>
    public const string ConnectionStringEnvironmentVariable = "INQUIRY_MYSQL_CONNECTION_STRING";

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

    public static async Task<MySqlTestHarness> CreateAsync(string? namePrefix = null)
    {
        var adminConnectionString = Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable)
            ?? throw new InvalidOperationException(
                $"Environment variable {ConnectionStringEnvironmentVariable} is not set; MySqlFactAttribute should have skipped this test.");

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
            // See remarks: make the SQLite-dialect (double-quoted) generated SQL resolve on MySQL.
            // Also keep MySqlConnector's default multi-statement support on for the emulated
            // INSERT ...; SELECT returning batches.
            AllowUserVariables = true,
        }.ToString();

        await using (var db = new MySqlConnection(connectionString))
        {
            await db.OpenAsync();
            await using (var mode = db.CreateCommand())
            {
                mode.CommandText = "SET SESSION sql_mode = CONCAT(@@sql_mode, ',ANSI_QUOTES');";
                await mode.ExecuteNonQueryAsync();
            }

            await using var cmd = db.CreateCommand();
            cmd.CommandText = NorthwindSchema.MySqlDdl;
            await cmd.ExecuteNonQueryAsync();
        }

        var services = new ServiceCollection()
            .AddInquiry()
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
