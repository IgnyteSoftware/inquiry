using Inquiry.Connections;
using MySqlConnector;

namespace Inquiry.MySql;

/// <summary>
/// Classifies <see cref="MySqlException"/>s by error number so the connection opener retries
/// transient connection faults (server unavailable, connection reset) but propagates terminal
/// ones (access denied, unknown database).
/// </summary>
internal sealed class MySqlTransientErrorDetector : ITransientErrorDetector
{
    /// <summary>
    /// MySQL error numbers that represent transient connection-open faults across managed cloud
    /// providers (AWS RDS, Azure Database for MySQL, Google Cloud SQL). These errors commonly
    /// surface during scaling events, maintenance windows, and failover.
    /// </summary>
    private static readonly HashSet<int> TransientErrorNumbers = new()
    {
        1040,   // ER_CON_COUNT_ERROR — Too many connections.
        1042,   // ER_BAD_HOST_ERROR — Can't get hostname for your address.
        1043,   // ER_HANDSHAKE_ERROR — Bad handshake.
        2002,   // CR_CONNECTION_ERROR — Can't connect to local MySQL server through socket.
        2003,   // CR_CONN_HOST_ERROR — Can't connect to MySQL server.
        2006,   // CR_SERVER_GONE_ERROR — MySQL server has gone away.
        2013,   // CR_SERVER_LOST — Lost connection to MySQL server during query.
    };

    /// <inheritdoc />
    public bool IsTransient(Exception exception)
    {
        return exception is MySqlException mysqlException
            && TransientErrorNumbers.Contains(mysqlException.Number);
    }
}
