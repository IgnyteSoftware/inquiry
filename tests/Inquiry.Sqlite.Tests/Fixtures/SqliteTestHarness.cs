using Inquiry.DependencyInjection;
using Inquiry.Sqlite.DependencyInjection;
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

    /// <summary>
    /// Spins up the harness and runs the supplied schema DDL on the keeper connection.
    /// </summary>
    public static async Task<SqliteTestHarness> CreateAsync(string schemaDdl, string? namePrefix = null)
    {
        var prefix = namePrefix ?? "Inquiry";
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = prefix + "_" + Guid.NewGuid().ToString("N"),
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared,
        }.ToString();

        var keeper = new SqliteConnection(connectionString);
        await keeper.OpenAsync();

        await using (var cmd = keeper.CreateCommand())
        {
            cmd.CommandText = schemaDdl;
            await cmd.ExecuteNonQueryAsync();
        }

        var services = new ServiceCollection()
            .AddInquiry()
            .AddInquirySqlite(connectionString)
            .BuildServiceProvider();

        return new SqliteTestHarness(connectionString, keeper, services);
    }

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
        await _keeper.DisposeAsync();
    }
}
