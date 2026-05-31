using Inquiry.Connections;
using Microsoft.Data.SqlClient;

namespace Inquiry.SqlServer;

/// <summary>
/// Classifies <see cref="SqlException"/>s by the documented Azure SQL transient error numbers so
/// the connection opener retries throttling / failover faults but propagates terminal ones (login
/// failures, bad object names, etc.).
/// </summary>
public sealed class SqlServerTransientErrorDetector : ITransientErrorDetector
{
    /// <summary>
    /// Azure SQL transient fault numbers (throttling, failover, connection-broker, and
    /// connection-reset conditions). Sources: Azure SQL "troubleshoot transient connection errors"
    /// guidance; the set intentionally aligns with EF Core's canonical
    /// <c>SqlServerTransientExceptionDetector</c> (hence the connection-level additions 20/64/4221),
    /// so it is a deliberate, slightly broader superset of the bare spec list rather than drift.
    /// </summary>
    private static readonly HashSet<int> TransientErrorNumbers = new()
    {
        -2,     // Timeout expired.
        20,     // Instance not currently configured to accept connections.
        64,     // Connection attempt failed (transport-level error during login).
        233,    // Connection initialization failed (no process on the other end of the pipe).
        4060,   // Cannot open database requested by the login.
        4221,   // Login to read-secondary failed (replica not available yet).
        10928,  // Resource limit reached for the database.
        10929,  // Server is too busy.
        40197,  // Service encountered an error processing the request; reconfiguration in progress.
        40501,  // Service is currently busy (engine throttling).
        40613,  // Database is currently unavailable.
        49918,  // Cannot process request; not enough resources.
        49919,  // Cannot process create/update request; too many operations in progress.
        49920,  // Cannot process request; too many operations in progress for subscription.
    };

    /// <inheritdoc />
    public bool IsTransient(Exception exception)
    {
        if (exception is not SqlException sqlException)
        {
            return false;
        }

        foreach (SqlError error in sqlException.Errors)
        {
            if (TransientErrorNumbers.Contains(error.Number))
            {
                return true;
            }
        }

        return false;
    }
}
