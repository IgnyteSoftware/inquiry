using Inquiry.Connections;
using Respawn;
using System.Data.Common;

namespace Inquiry.Testing;

/// <summary>
/// A thin wrapper over Respawn (<see cref="Respawner"/>) for resetting database state between
/// integration tests, with overloads that pair naturally with Inquiry's
/// <see cref="IInquiryConnectionFactory"/>.
/// </summary>
/// <remarks>
/// Respawn supports SQL Server, PostgreSQL, MySQL, and Oracle — it does <b>not</b> support
/// SQLite. For SQLite, use a fresh <see cref="SqliteInquiryFixture"/> per test instead. The
/// underlying <see cref="Respawner"/> caches the schema graph at creation time, but each
/// <c>ResetAsync</c> call still needs an open connection — hence the factory-based reset
/// overload that opens and disposes one around the reset.
/// </remarks>
public sealed class InquiryRespawner
{
    private readonly Respawner _respawner;

    private InquiryRespawner(Respawner respawner) => _respawner = respawner;

    /// <summary>
    /// Creates a respawner from an already-open connection, building Respawn's cached delete
    /// graph from the database schema.
    /// </summary>
    /// <param name="openConnection">An open connection to the database to reset.</param>
    /// <param name="options">
    /// Optional Respawn options (e.g. <see cref="RespawnerOptions.TablesToIgnore"/>,
    /// <see cref="RespawnerOptions.DbAdapter"/>). Defaults to <see cref="RespawnerOptions"/>'s
    /// defaults (SQL Server adapter) when omitted.
    /// </param>
    public static async Task<InquiryRespawner> CreateAsync(DbConnection openConnection, RespawnerOptions? options = null)
    {
        if (openConnection is null)
        {
            throw new ArgumentNullException(nameof(openConnection));
        }

        var respawner = await Respawner.CreateAsync(openConnection, options ?? new RespawnerOptions()).ConfigureAwait(false);
        return new InquiryRespawner(respawner);
    }

    /// <summary>
    /// Creates a respawner by opening a connection through the registered Inquiry connection
    /// factory. The connection is disposed once the schema graph has been built.
    /// </summary>
    /// <param name="factory">The Inquiry connection factory to open the connection with.</param>
    /// <param name="options">Optional Respawn options; see the <see cref="DbConnection"/> overload.</param>
    public static async Task<InquiryRespawner> CreateAsync(IInquiryConnectionFactory factory, RespawnerOptions? options = null)
    {
        if (factory is null)
        {
            throw new ArgumentNullException(nameof(factory));
        }

        var connection = await factory.OpenConnectionAsync().ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            return await CreateAsync(connection, options).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Resets the database using the supplied open connection.
    /// </summary>
    /// <param name="openConnection">An open connection to the database to reset.</param>
    public Task ResetAsync(DbConnection openConnection)
    {
        if (openConnection is null)
        {
            throw new ArgumentNullException(nameof(openConnection));
        }

        return _respawner.ResetAsync(openConnection);
    }

    /// <summary>
    /// Opens a connection through the Inquiry connection factory, resets the database, and
    /// disposes the connection.
    /// </summary>
    /// <param name="factory">The Inquiry connection factory to open the connection with.</param>
    public async Task ResetAsync(IInquiryConnectionFactory factory)
    {
        if (factory is null)
        {
            throw new ArgumentNullException(nameof(factory));
        }

        var connection = await factory.OpenConnectionAsync().ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            await _respawner.ResetAsync(connection).ConfigureAwait(false);
        }
    }
}
