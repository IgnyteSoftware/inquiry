using Inquiry.DependencyInjection;
using Inquiry.Sqlite.DependencyInjection;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Inquiry.Testing;

/// <summary>
/// A self-contained Inquiry test fixture backed by a unique shared-cache in-memory SQLite
/// database. The fixture opens a keeper connection that holds the database alive for the
/// fixture's lifetime and builds a <see cref="ServiceProvider"/> with the Inquiry runtime and
/// the SQLite provider registered. Each fixture gets its own database, so tests stay isolated
/// by creating one fixture per test (or per test class, when sharing is intended).
/// </summary>
/// <remarks>
/// Generated store registrations live in the consuming assembly, so the fixture cannot register
/// them itself. Pass them via the <c>configureServices</c> callback:
/// <code>
/// await using var fixture = await SqliteInquiryFixture.CreateAsync(
///     services => services.AddInquiryGeneratedStores());
/// </code>
/// </remarks>
public sealed class SqliteInquiryFixture : IAsyncDisposable
{
    private readonly SqliteConnection _keeper;
    private readonly ServiceProvider _provider;

    private SqliteInquiryFixture(string connectionString, SqliteConnection keeper, ServiceProvider provider)
    {
        ConnectionString = connectionString;
        _keeper = keeper;
        _provider = provider;
    }

    /// <summary>
    /// Gets the connection string for the fixture's in-memory database. Additional connections
    /// opened with this string (e.g. by the registered Inquiry connection factory) share the
    /// same database while the fixture is alive.
    /// </summary>
    public string ConnectionString { get; }

    /// <summary>
    /// Gets the root service provider with Inquiry, the SQLite provider, and any
    /// caller-supplied registrations applied.
    /// </summary>
    public IServiceProvider Services => _provider;

    /// <summary>
    /// Creates the fixture: generates a unique in-memory database, opens the keeper connection,
    /// and builds the service provider.
    /// </summary>
    /// <param name="configureServices">
    /// Optional callback for additional registrations — e.g. the generated
    /// <c>AddInquiryGeneratedStores()</c> extension, interceptors, or logging.
    /// </param>
    public static async Task<SqliteInquiryFixture> CreateAsync(Action<IServiceCollection>? configureServices = null)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = "InquiryTesting_" + Guid.NewGuid().ToString("N"),
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared,
        };
        var connectionString = builder.ToString();

        var keeper = new SqliteConnection(connectionString);
        await keeper.OpenAsync().ConfigureAwait(false);

        var services = new ServiceCollection();
        services.AddInquiry();
        services.AddInquirySqlite(connectionString);
        configureServices?.Invoke(services);

        return new SqliteInquiryFixture(connectionString, keeper, services.BuildServiceProvider());
    }

    /// <summary>
    /// Creates a new service scope. Use this to resolve scoped Inquiry services
    /// (<see cref="IInquiry"/>, generated stores) the same way application code would.
    /// </summary>
    public IServiceScope CreateScope() => _provider.CreateScope();

    /// <summary>
    /// Executes DDL (or any non-query SQL) against the fixture database through the keeper
    /// connection. Intended for per-test schema setup such as <c>CREATE TABLE</c>.
    /// </summary>
    public async Task ExecuteDdlAsync(string ddl)
    {
        if (string.IsNullOrWhiteSpace(ddl))
        {
            throw new ArgumentException("DDL cannot be empty.", nameof(ddl));
        }

        var command = _keeper.CreateCommand();
        await using (command.ConfigureAwait(false))
        {
            command.CommandText = ddl;
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Disposes the service provider and the keeper connection, releasing the in-memory database.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync().ConfigureAwait(false);
        await _keeper.DisposeAsync().ConfigureAwait(false);
    }
}
