using Inquiry.DependencyInjection;
using Inquiry.Northwind;
using Inquiry.SqlServer.DependencyInjection;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Inquiry.SqlServer.Tests.Fixtures;

/// <summary>
/// Creates a throwaway SQL Server database, runs the Northwind DDL against it, and exposes
/// a configured <see cref="ServiceProvider"/>. The database is dropped on disposal so
/// parallel test classes never collide on table state.
/// </summary>
internal sealed class SqlServerTestHarness : IAsyncDisposable
{
    /// <summary>
    /// Connection string to a database the test process can use to <c>CREATE DATABASE</c>
    /// (typically <c>master</c>). When unset, <see cref="SqlServerFactAttribute"/> skips the
    /// test rather than failing it.
    /// </summary>
    public const string ConnectionStringEnvironmentVariable = "INQUIRY_SQLSERVER_CONNECTION_STRING";

    private readonly string _adminConnectionString;
    private readonly string _databaseName;

    private SqlServerTestHarness(string adminConnectionString, string databaseName, string connectionString, ServiceProvider services)
    {
        _adminConnectionString = adminConnectionString;
        _databaseName = databaseName;
        ConnectionString = connectionString;
        Services = services;
    }

    public string ConnectionString { get; }

    public ServiceProvider Services { get; }

    public T GetRequiredService<T>() where T : notnull => Services.GetRequiredService<T>();

    public static async Task<SqlServerTestHarness> CreateAsync(string? namePrefix = null)
    {
        var adminConnectionString = Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable)
            ?? throw new InvalidOperationException(
                $"Environment variable {ConnectionStringEnvironmentVariable} is not set; SqlServerFactAttribute should have skipped this test.");

        var prefix = namePrefix ?? "Inquiry";
        var databaseName = prefix + "_" + Guid.NewGuid().ToString("N");

        await using (var admin = new SqlConnection(adminConnectionString))
        {
            await admin.OpenAsync();
            await using var cmd = admin.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE [{databaseName}];";
            await cmd.ExecuteNonQueryAsync();
        }

        var connectionString = new SqlConnectionStringBuilder(adminConnectionString)
        {
            InitialCatalog = databaseName,
        }.ToString();

        await using (var db = new SqlConnection(connectionString))
        {
            await db.OpenAsync();
            await using var cmd = db.CreateCommand();
            cmd.CommandText = NorthwindSchema.SqlServerDdl;
            await cmd.ExecuteNonQueryAsync();
        }

        var services = new ServiceCollection()
            .AddInquiry()
            .AddInquirySqlServer(connectionString)
            .BuildServiceProvider();

        return new SqlServerTestHarness(adminConnectionString, databaseName, connectionString, services);
    }

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();

        // Force-close pooled connections so DROP DATABASE doesn't fail with "database in use".
        SqlConnection.ClearAllPools();

        try
        {
            await using var admin = new SqlConnection(_adminConnectionString);
            await admin.OpenAsync();
            await using var cmd = admin.CreateCommand();
            cmd.CommandText =
                $"IF DB_ID(N'{_databaseName}') IS NOT NULL " +
                $"BEGIN " +
                $"  ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; " +
                $"  DROP DATABASE [{_databaseName}]; " +
                $"END;";
            await cmd.ExecuteNonQueryAsync();
        }
        catch
        {
            // Best-effort cleanup. Don't fail the test on teardown.
        }
    }
}
