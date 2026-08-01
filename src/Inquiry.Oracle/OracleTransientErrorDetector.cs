using Inquiry.Connections;
using Oracle.ManagedDataAccess.Client;

namespace Inquiry.Oracle;

/// <summary>
/// Classifies <see cref="OracleException"/>s by error number so the connection opener retries
/// transient connection faults (instance unavailable, listener down, connection lost) but
/// propagates terminal ones (invalid credentials, tablespace full).
/// </summary>
internal sealed class OracleTransientErrorDetector : ITransientErrorDetector
{
    /// <summary>
    /// Oracle error numbers that represent transient connection-open faults across managed cloud
    /// providers (OCI Autonomous Database, AWS RDS Oracle). These errors commonly surface during
    /// scaling events, maintenance windows, and failover.
    /// </summary>
    private static readonly HashSet<int> TransientErrorNumbers = new()
    {
        1033,   // ORA-01033 — Oracle initialization or shutdown in progress.
        1034,   // ORA-01034 — Oracle not available.
        1089,   // ORA-01089 — Immediate shutdown or close in progress.
        3113,   // ORA-03113 — End-of-file on communication channel.
        3114,   // ORA-03114 — Not connected to ORACLE.
        3135,   // ORA-03135 — Connection lost contact.
        12170,  // ORA-12170 — TNS connect timeout.
        12505,  // ORA-12505 — TNS listener does not currently know of SID.
        12541,  // ORA-12541 — TNS no listener.
    };

    /// <inheritdoc />
    public bool IsTransient(Exception exception)
    {
        return exception is OracleException oracleException
            && TransientErrorNumbers.Contains(oracleException.Number);
    }
}
