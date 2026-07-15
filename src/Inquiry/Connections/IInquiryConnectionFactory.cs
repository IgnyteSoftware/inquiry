using System.Data.Common;
using Inquiry.Commands;

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
    /// Applies provider-specific fixups to a command after the pipeline has bound its parameters but
    /// before execution. The default implementation is a no-op; providers override it for parameter
    /// adjustments that depend on the bound set — e.g. Oracle strips the dialect-agnostic <c>@</c>
    /// sigil from parameter names so they bind by name against the <c>:name</c> references in its SQL.
    /// </summary>
    void FinalizeCommand(DbCommand command)
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

    /// <summary>
    /// Gets whether the pipeline may execute multi-item writes through <see cref="DbBatch"/> when
    /// the provider's connection reports <see cref="DbConnection.CanCreateBatch"/>. Defaults to
    /// <see langword="true"/>. Providers whose <see cref="FinalizeCommand"/> rewrites parameters on
    /// the bound <see cref="DbCommand"/> (e.g. renaming or value coercion) must return
    /// <see langword="false"/>: the DbBatch path binds parameters onto
    /// <see cref="DbBatchCommand"/> instances and never calls <see cref="FinalizeCommand"/>, so
    /// those fixups would be silently skipped. Returning <see langword="false"/> routes batches
    /// through the sequential per-command path instead.
    /// </summary>
    bool SupportsBatchExecution => true;

    /// <summary>Gets the provider-selected batch execution strategy.</summary>
    InquiryBatchExecutionMode BatchExecutionMode
        => SupportsBatchExecution ? InquiryBatchExecutionMode.DbBatch : InquiryBatchExecutionMode.ReusedCommand;

    /// <summary>Initializes a provider batch command before the generated whole-chunk binder runs.</summary>
    void InitializeBatchChunkCommand(DbCommand command, int itemCount)
    {
    }
}
