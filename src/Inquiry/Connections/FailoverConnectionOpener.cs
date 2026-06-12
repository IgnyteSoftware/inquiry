using System.Data.Common;

namespace Inquiry.Connections;

/// <summary>
/// Opens a connection against a primary connection string and, when that fails for any
/// non-cancellation reason (after any configured open-time retry), falls back to a secondary
/// "backup server" connection string. Used by provider connection factories when a failover
/// connection string is configured.
/// </summary>
/// <remarks>
/// Failover applies per open: every open tries the primary first, so traffic returns to the
/// primary automatically once it recovers. If both opens fail, an <see cref="AggregateException"/>
/// carrying both faults is thrown.
/// </remarks>
internal static class FailoverConnectionOpener
{
    public static async ValueTask<DbConnection> OpenAsync(
        Func<string, CancellationToken, ValueTask<DbConnection>> open,
        string primaryConnectionString,
        string failoverConnectionString,
        RetryingConnectionOpener? retryingOpener,
        CancellationToken cancellationToken)
    {
        Exception primaryException;
        try
        {
            return await OpenOneAsync(open, primaryConnectionString, retryingOpener, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            primaryException = exception;
        }

        try
        {
            return await OpenOneAsync(open, failoverConnectionString, retryingOpener, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception failoverException)
        {
            throw new AggregateException(
                "Opening a connection failed against both the primary and the failover server.",
                primaryException,
                failoverException);
        }
    }

    private static ValueTask<DbConnection> OpenOneAsync(
        Func<string, CancellationToken, ValueTask<DbConnection>> open,
        string connectionString,
        RetryingConnectionOpener? retryingOpener,
        CancellationToken cancellationToken)
    {
        return retryingOpener is null
            ? open(connectionString, cancellationToken)
            : retryingOpener.OpenAsync(ct => open(connectionString, ct), cancellationToken);
    }
}
