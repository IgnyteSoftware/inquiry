using System.Data.Common;

namespace Inquiry.Connections;

/// <summary>
/// Creates and opens database connections for generated Inquiry stores.
/// </summary>
public interface IInquiryConnectionFactory
{
    /// <summary>
    /// Opens a database connection.
    /// </summary>
    ValueTask<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies provider-specific setup to a freshly created <see cref="DbCommand"/> before the
    /// pipeline binds parameters or executes it. The default implementation is a no-op; providers
    /// override it for command-level configuration (e.g. <c>BindByName</c> on Oracle). Called after
    /// every <c>DbConnection.CreateCommand()</c> in both pipelines.
    /// </summary>
    void InitializeCommand(DbCommand command)
    {
    }

    /// <summary>
    /// Gets whether prepared statements created on a connection survive the connection's lifecycle
    /// (e.g. a pool-level / server-side prepared-statement cache). When <see langword="true"/> and
    /// <see cref="PreparedStatementMode.Auto"/> is configured, the pipeline issues
    /// <see cref="DbCommand.PrepareAsync"/> for non-stored-procedure commands. Defaults to
    /// <see langword="false"/>; only providers with persistent prepared-statement caches (Npgsql)
    /// should return <see langword="true"/>.
    /// </summary>
    bool SupportsPersistentPreparedStatements => false;
}
