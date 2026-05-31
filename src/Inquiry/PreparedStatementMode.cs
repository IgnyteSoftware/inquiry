namespace Inquiry;

/// <summary>
/// Controls whether the Inquiry pipeline issues <see cref="System.Data.Common.DbCommand.PrepareAsync"/>
/// before executing generated commands.
/// </summary>
public enum PreparedStatementMode
{
    /// <summary>
    /// Never prepare commands (default). The provider's own plan cache / auto-prepare handles reuse.
    /// </summary>
    None = 0,

    /// <summary>
    /// Prepare commands automatically, but only on providers whose prepared state survives the
    /// connection lifecycle (see <see cref="Connections.IInquiryConnectionFactory.SupportsPersistentPreparedStatements"/>)
    /// and only for non-stored-procedure commands.
    /// </summary>
    Auto = 1,
}
