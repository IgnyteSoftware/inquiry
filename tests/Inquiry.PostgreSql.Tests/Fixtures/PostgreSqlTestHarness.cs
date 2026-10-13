using Inquiry.DependencyInjection;
using Inquiry.FeatureCatalog;
using Inquiry.Northwind;
using Inquiry.Northwind.Stores;
using Inquiry.PostgreSql.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using System.Runtime.ExceptionServices;

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

    public static async Task<PostgreSqlTestHarness> CreateFromDdlAsync(
        string adminConnectionString,
        string ddl,
        string? namePrefix = null,
        Action<InquiryOptions>? configureOptions = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var prefix = (namePrefix ?? "inquiry").ToLowerInvariant();
        var databaseName = prefix + "_" + Guid.NewGuid().ToString("N");
        var wasCreated = false;

        try
        {
            await using (var admin = new NpgsqlConnection(adminConnectionString))
            {
                await admin.OpenAsync();
                await using var cmd = admin.CreateCommand();
                // Identifier quoted so PostgreSQL preserves casing exactly; PostgreSQL identifiers
                // are otherwise folded to lowercase.
                cmd.CommandText = $"CREATE DATABASE \"{databaseName}\";";
                await cmd.ExecuteNonQueryAsync();
                wasCreated = true;
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

            var serviceCollection = new ServiceCollection();
            if (configureOptions is null)
                serviceCollection.AddInquiry(typeof(CustomerStore).Assembly, typeof(GuidItemStore).Assembly, typeof(VersionedItemStore).Assembly);
            else
                serviceCollection.AddInquiry(configureOptions, typeof(CustomerStore).Assembly, typeof(GuidItemStore).Assembly, typeof(VersionedItemStore).Assembly);
            serviceCollection.AddInquiryPostgreSql(connectionString);
            configureServices?.Invoke(serviceCollection);
            var services = serviceCollection.BuildServiceProvider();

            return new PostgreSqlTestHarness(adminConnectionString, databaseName, connectionString, services);
        }
        catch (Exception setupException) when (wasCreated)
        {
            NpgsqlConnection.ClearAllPools();
            try
            {
                await DropDatabaseAsync(adminConnectionString, databaseName);
            }
            catch (Exception cleanupException)
            {
                throw new AggregateException(
                    $"PostgreSQL test database '{databaseName}' setup and cleanup both failed.",
                    setupException,
                    cleanupException);
            }

            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        Exception? serviceFailure = null;
        try
        {
            await Services.DisposeAsync();
        }
        catch (Exception ex)
        {
            serviceFailure = ex;
        }

        // Force-close pooled connections so DROP DATABASE doesn't fail with "database in use".
        NpgsqlConnection.ClearAllPools();

        try
        {
            await DropDatabaseAsync(_adminConnectionString, _databaseName);
        }
        catch (Exception cleanupException) when (serviceFailure is not null)
        {
            throw new AggregateException(
                $"PostgreSQL test database '{_databaseName}' service disposal and cleanup both failed.",
                serviceFailure,
                cleanupException);
        }
        catch
        {
            // Best-effort cleanup. Don't fail the test on teardown.
        }

        if (serviceFailure is not null)
            ExceptionDispatchInfo.Throw(serviceFailure);
    }

    private static async Task DropDatabaseAsync(string adminConnectionString, string databaseName)
    {
        await using var admin = new NpgsqlConnection(adminConnectionString);
        await admin.OpenAsync();
        await using var cmd = admin.CreateCommand();
        cmd.CommandText = $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE);";
        await cmd.ExecuteNonQueryAsync();
    }
}
