namespace Inquiry.Connections;

/// <summary>
/// Classifies exceptions thrown while opening a database connection as transient (worth retrying)
/// or terminal. Implementations are provider-specific: each cloud engine documents its own set of
/// transient error codes / SQLSTATEs.
/// </summary>
internal interface ITransientErrorDetector
{
    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="exception"/> represents a transient
    /// fault that is likely to succeed on a retry (e.g. a throttling, failover, or
    /// connection-reset condition); otherwise <see langword="false"/>.
    /// </summary>
    bool IsTransient(Exception exception);
}
