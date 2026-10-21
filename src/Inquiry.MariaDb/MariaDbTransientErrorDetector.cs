using Inquiry.Connections;
using MySqlConnector;

namespace Inquiry.MariaDb;

/// <summary>
/// Classifies <see cref="MySqlException"/>s by error number so the connection opener retries
/// transient connection faults (server unavailable, connection reset) but propagates terminal
/// ones (access denied, unknown database). MariaDB and MySQL share the same wire protocol and
/// error-number space (via MySqlConnector), so the transient set is identical.
/// </summary>
internal sealed class MariaDbTransientErrorDetector : ITransientErrorDetector
{
    /// <summary>
    /// MySQL/MariaDB error numbers that represent transient connection-open faults across managed
    /// cloud providers (AWS RDS, Azure Database for MariaDB, SkySQL). These errors commonly
    /// surface during scaling events, maintenance windows, and failover.
    /// </summary>
    private static readonly HashSet<int> TransientErrorNumbers = new()
    {
        1040,   // ER_CON_COUNT_ERROR — Too many connections.
        1042,   // ER_BAD_HOST_ERROR — Can't get hostname for your address.
        1043,   // ER_HANDSHAKE_ERROR — Bad handshake.
        2002,   // CR_CONNECTION_ERROR — Can't connect to local server through socket.
        2003,   // CR_CONN_HOST_ERROR — Can't connect to server.
        2006,   // CR_SERVER_GONE_ERROR — Server has gone away.
        2013,   // CR_SERVER_LOST — Lost connection to server during query.
    };

    /// <inheritdoc />
    public bool IsTransient(Exception exception)
    {
        return exception is MySqlException mysqlException
            && TransientErrorNumbers.Contains(mysqlException.Number);
    }
}
