using Inquiry.Connections;
using Npgsql;

namespace Inquiry.PostgreSql;

/// <summary>
/// Classifies Amazon Aurora PostgreSQL connection-open faults as transient. Aurora failover drops
/// the connection to the old writer, so connection-class SQLSTATEs (<c>08xxx</c>), admin shutdown
/// (<c>57P01</c>), and transport-level <see cref="NpgsqlException"/>s flagged transient by Npgsql
/// are retried; everything else propagates.
/// </summary>
internal sealed class AuroraTransientErrorDetector : ITransientErrorDetector
{
    private static readonly HashSet<string> TransientSqlStates = new(StringComparer.Ordinal)
    {
        "57P01", // admin_shutdown
    };

    /// <inheritdoc />
    public bool IsTransient(Exception exception)
    {
        if (exception is PostgresException postgresException)
        {
            // Connection-exception class (08xxx) covers connection_failure / does-not-exist /
            // rejected during failover.
            return postgresException.SqlState.StartsWith("08", StringComparison.Ordinal)
                || TransientSqlStates.Contains(postgresException.SqlState);
        }

        // Transport-level faults (socket reset on failover) surface as a base NpgsqlException.
        return exception is NpgsqlException npgsqlException && npgsqlException.IsTransient;
    }
}
