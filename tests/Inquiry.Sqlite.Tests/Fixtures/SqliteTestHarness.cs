using Inquiry.DependencyInjection;
using Inquiry.Sqlite.DependencyInjection;
using Inquiry.Northwind.Stores;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Inquiry.Sqlite.Tests.Fixtures;

/// <summary>
/// Owns a uniquely-named shared in-memory SQLite database plus a configured
/// <see cref="ServiceProvider"/>. The "keeper" connection is held open so the
/// shared in-memory DB persists for the lifetime of this harness; disposing
/// releases everything in the right order.
/// </summary>
internal sealed class SqliteTestHarness : IAsyncDisposable
{
    private readonly SqliteConnection _keeper;

    private SqliteTestHarness(string connectionString, SqliteConnection keeper, ServiceProvider services)
    {
        ConnectionString = connectionString;
        _keeper = keeper;
        Services = services;
    }

    public string ConnectionString { get; }

    public ServiceProvider Services { get; }

    public T GetRequiredService<T>() where T : notnull => Services.GetRequiredService<T>();

    /// <summary>Runs a raw scalar query against the shared in-memory database (test assertions only).</summary>
    public async Task<object?> ExecuteScalarAsync(string sql)
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        var result = await cmd.ExecuteScalarAsync();
        return result is DBNull ? null : result;
    }

    /// <summary>
    /// Spins up the harness and runs the supplied schema DDL on the keeper connection.
    /// </summary>
    /// <param name="schemaDdl">The DDL to execute against the new database.</param>
    /// <param name="namePrefix">Optional prefix for the in-memory database name.</param>
    /// <param name="foreignKeys">
    /// When false, sets <c>Foreign Keys=False</c> on the connection string so SQLite skips
    /// FK enforcement. Use for the rare tests that intentionally insert orphan-FK rows.
    /// Defaults to true (FK enforcement on), matching Microsoft.Data.Sqlite's default.
    /// </param>
    public static async Task<SqliteTestHarness> CreateAsync(
        string schemaDdl,
        string? namePrefix = null,
        bool foreignKeys = true,
        Action<InquiryOptions>? configureOptions = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var prefix = namePrefix ?? "Inquiry";
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = prefix + "_" + Guid.NewGuid().ToString("N"),
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared,
            ForeignKeys = foreignKeys,
        }.ToString();

        var keeper = new SqliteConnection(connectionString);
        await keeper.OpenAsync();

        await using (var cmd = keeper.CreateCommand())
        {
            cmd.CommandText = schemaDdl;
            await cmd.ExecuteNonQueryAsync();
        }

        var serviceCollection = new ServiceCollection();
        if (configureOptions is null)
            serviceCollection.AddInquiry(typeof(CustomerStore).Assembly, typeof(GeneratedItemStore).Assembly);
        else
            serviceCollection.AddInquiry(configureOptions, typeof(CustomerStore).Assembly, typeof(GeneratedItemStore).Assembly);
        serviceCollection.AddInquirySqlite(connectionString);
        configureServices?.Invoke(serviceCollection);
        var services = serviceCollection.BuildServiceProvider();

        return new SqliteTestHarness(connectionString, keeper, services);
    }

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
        await _keeper.DisposeAsync();
    }
}
