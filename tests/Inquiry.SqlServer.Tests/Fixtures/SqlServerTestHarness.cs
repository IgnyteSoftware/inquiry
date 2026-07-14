using Inquiry.DependencyInjection;
using Inquiry.FeatureCatalog;
using Inquiry.Northwind;
using Inquiry.Northwind.Stores;
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

    public static Task<SqlServerTestHarness> CreateAsync(string adminConnectionString, string? namePrefix = null)
        => CreateFromDdlAsync(adminConnectionString, NorthwindSchema.SqlServerDdl, namePrefix);

    public static async Task<SqlServerTestHarness> CreateFromDdlAsync(
        string adminConnectionString,
        string ddl,
        string? namePrefix = null,
        bool provisionProviderArtifacts = true,
        Action<string>? databaseCreated = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var prefix = namePrefix ?? "Inquiry";
        var databaseName = prefix + "_" + Guid.NewGuid().ToString("N");
        var wasCreated = false;

        try
        {
            await using (var admin = new SqlConnection(adminConnectionString))
            {
                await admin.OpenAsync();
                await using var cmd = admin.CreateCommand();
                cmd.CommandText = $"CREATE DATABASE [{databaseName.Replace("]", "]]", StringComparison.Ordinal)}];";
                await cmd.ExecuteNonQueryAsync();
                wasCreated = true;
                databaseCreated?.Invoke(databaseName);
            }

            var connectionString = new SqlConnectionStringBuilder(adminConnectionString)
            {
                InitialCatalog = databaseName,
            }.ToString();

            await using (var db = new SqlConnection(connectionString))
            {
                await db.OpenAsync();
                if (provisionProviderArtifacts)
                {
                    await using var artifacts = db.CreateCommand();
                    artifacts.CommandText = global::Inquiry.Generated.InquiryGeneratedSchema.ProviderArtifactsDdl;
                    await artifacts.ExecuteNonQueryAsync();
                }

                await using var cmd = db.CreateCommand();
                cmd.CommandText = ddl;
                await cmd.ExecuteNonQueryAsync();
            }

            var serviceCollection = new ServiceCollection()
                .AddInquiry(typeof(CustomerStore).Assembly, typeof(GuidItemStore).Assembly, typeof(VersionedItemStore).Assembly)
                .AddInquirySqlServer(connectionString);
            configureServices?.Invoke(serviceCollection);
            var services = serviceCollection.BuildServiceProvider();

            return new SqlServerTestHarness(adminConnectionString, databaseName, connectionString, services);
        }
        catch (Exception setupException) when (wasCreated)
        {
            SqlConnection.ClearAllPools();
            try
            {
                await DropDatabaseAsync(adminConnectionString, databaseName);
            }
            catch (Exception cleanupException)
            {
                throw new AggregateException(
                    $"SQL Server test database '{databaseName}' setup and cleanup both failed.",
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
        SqlConnection.ClearAllPools();

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
        var literalName = databaseName.Replace("'", "''", StringComparison.Ordinal);
        var quotedName = databaseName.Replace("]", "]]", StringComparison.Ordinal);

        await using var admin = new SqlConnection(adminConnectionString);
        await admin.OpenAsync();
        await using var cmd = admin.CreateCommand();
        cmd.CommandText =
            $"IF DB_ID(N'{literalName}') IS NOT NULL " +
            $"BEGIN " +
            $"  ALTER DATABASE [{quotedName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; " +
            $"  DROP DATABASE [{quotedName}]; " +
            $"END;";
        await cmd.ExecuteNonQueryAsync();
    }
}
