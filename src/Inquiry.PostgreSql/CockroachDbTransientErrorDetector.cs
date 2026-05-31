using Inquiry.Connections;
using Npgsql;

namespace Inquiry.PostgreSql;

/// <summary>
/// Classifies CockroachDB connection-open faults as transient by SQLSTATE: <c>40001</c>
/// (serialization failure / retryable transaction), <c>08006</c> (connection failure), and
/// <c>57P01</c> (admin shutdown). Other faults propagate.
/// </summary>
public sealed class CockroachDbTransientErrorDetector : ITransientErrorDetector
{
    private static readonly HashSet<string> TransientSqlStates = new(StringComparer.Ordinal)
    {
        "40001", // serialization_failure (Cockroach retryable transaction)
        "08006", // connection_failure
        "57P01", // admin_shutdown
    };

    /// <inheritdoc />
    public bool IsTransient(Exception exception) =>
        exception is PostgresException postgresException
        && TransientSqlStates.Contains(postgresException.SqlState);
}
