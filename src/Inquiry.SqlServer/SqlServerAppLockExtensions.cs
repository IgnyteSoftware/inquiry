using Inquiry.Commands;
using Inquiry.Parameters;
using Inquiry.Transactions;

namespace Inquiry.SqlServer;

/// <summary>
/// SQL Server application locks (<c>sp_getapplock</c>) scoped to an Inquiry transaction.
/// </summary>
public static class SqlServerAppLockExtensions
{
    private const int MaxResourceLength = 255;

    // The client command timeout must outlast the server-side lock wait, or SqlClient cancels the
    // wait first and reports a command timeout instead of the lock timeout.
    private const int CommandTimeoutMarginSeconds = 30;

    private const string AcquireSql =
        "DECLARE @result int; " +
        "EXEC @result = sys.sp_getapplock @Resource = @resource, @LockMode = @mode, @LockOwner = N'Transaction', @LockTimeout = @timeout; " +
        "SELECT @result;";

    /// <summary>
    /// Acquires a SQL Server application lock owned by this transaction. SQL Server releases the
    /// lock when the outermost transaction commits or rolls back. A rollback to a savepoint also
    /// releases a lock acquired after that savepoint.
    /// </summary>
    /// <param name="transaction">An Inquiry transaction on a SQL Server connection. A savepoint
    /// handle acquires the lock for its outer transaction.</param>
    /// <param name="resource">The lock name, 1 to 255 characters. SQL Server compares names
    /// binary, so the comparison is case-sensitive.</param>
    /// <param name="mode">The lock mode.</param>
    /// <param name="timeout">How long to wait for the lock. <see cref="TimeSpan.Zero"/> does not
    /// wait. <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> waits until the lock is granted
    /// or <paramref name="cancellationToken"/> is canceled. Sub-millisecond values round up.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>A task that completes when the lock is granted (return code 0 or 1).</returns>
    /// <exception cref="InquiryAppLockException"><c>sp_getapplock</c> returned -1, -2, -3 or -999.
    /// <see cref="InquiryAppLockException.Failure"/> names the outcome.</exception>
    /// <exception cref="ArgumentException"><paramref name="resource"/> is empty or longer than 255 characters.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="mode"/> is undefined, or
    /// <paramref name="timeout"/> is negative (other than infinite) or longer than <see cref="int.MaxValue"/> milliseconds.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public static async Task AcquireAppLockAsync(
        this IInquiryTransaction transaction,
        string resource,
        InquiryAppLockMode mode,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentException.ThrowIfNullOrEmpty(resource);
        if (resource.Length > MaxResourceLength)
        {
            throw new ArgumentException($"The application lock resource name cannot be longer than {MaxResourceLength} characters.", nameof(resource));
        }

        var modeName = mode switch
        {
            InquiryAppLockMode.Shared => "Shared",
            InquiryAppLockMode.Update => "Update",
            InquiryAppLockMode.IntentShared => "IntentShared",
            InquiryAppLockMode.IntentExclusive => "IntentExclusive",
            InquiryAppLockMode.Exclusive => "Exclusive",
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown application lock mode."),
        };

        int lockTimeoutMs;
        int commandTimeoutSeconds;
        if (timeout == Timeout.InfiniteTimeSpan)
        {
            lockTimeoutMs = -1;
            commandTimeoutSeconds = 0;
        }
        else if (timeout < TimeSpan.Zero || Math.Ceiling(timeout.TotalMilliseconds) > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), timeout, "The lock timeout must be zero or positive and at most int.MaxValue milliseconds, or Timeout.InfiniteTimeSpan.");
        }
        else
        {
            lockTimeoutMs = (int)Math.Ceiling(timeout.TotalMilliseconds);
            commandTimeoutSeconds = (int)Math.Ceiling(lockTimeoutMs / 1000d) + CommandTimeoutMarginSeconds;
        }

        var command = new InquiryCommand(
            AcquireSql,
            [
                new InquiryParameter("resource", resource, System.Data.DbType.String, size: MaxResourceLength),
                new InquiryParameter("mode", modeName, System.Data.DbType.AnsiString, size: 32),
                new InquiryParameter("timeout", lockTimeoutMs, System.Data.DbType.Int32),
            ],
            commandTimeout: commandTimeoutSeconds);

        var result = await transaction.ExecuteScalarAsync<int>(command, cancellationToken).ConfigureAwait(false);
        if (result is 0 or 1)
        {
            return;
        }

        throw new InquiryAppLockException(resource, (InquiryAppLockFailure)result);
    }
}
