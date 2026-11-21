namespace Inquiry.SqlServer;

/// <summary>
/// Thrown by <see cref="SqlServerInquiryTransactionExtensions.AcquireAppLockAsync"/> when <c>sp_getapplock</c>
/// returns a negative result, so the transaction did not get the requested mode. A lock that the
/// transaction already held on the resource, such as a <c>Shared</c> lock it tried to convert to
/// <c>Exclusive</c>, stays held. The transaction stays open; the caller decides whether to roll it back.
/// </summary>
public sealed class SqlServerAppLockException : Exception
{
    internal SqlServerAppLockException(string resource, SqlServerAppLockFailure failure)
        : base(CreateMessage(resource, failure))
    {
        Resource = resource;
        Failure = failure;
    }

    /// <summary>Gets the lock resource name.</summary>
    public string Resource { get; }

    /// <summary>Gets the outcome <c>sp_getapplock</c> reported.</summary>
    public SqlServerAppLockFailure Failure { get; }

    /// <summary>Gets the raw <c>sp_getapplock</c> return code.</summary>
    public int ReturnCode => (int)Failure;

    private static string CreateMessage(string resource, SqlServerAppLockFailure failure)
    {
        var reason = failure switch
        {
            SqlServerAppLockFailure.Timeout => "the lock was not granted within the timeout",
            SqlServerAppLockFailure.Canceled => "SQL Server canceled the lock request",
            SqlServerAppLockFailure.Deadlock => "the lock request was chosen as a deadlock victim",
            SqlServerAppLockFailure.InvalidRequest => "SQL Server rejected the request as invalid",
            _ => "sp_getapplock returned an undocumented result",
        };
        return $"Application lock '{resource}' was not acquired: {reason} (sp_getapplock returned {(int)failure}).";
    }
}
