namespace Inquiry.SqlServer;

/// <summary>
/// Thrown by <see cref="SqlServerAppLockExtensions.AcquireAppLockAsync"/> when <c>sp_getapplock</c>
/// returns a negative result, so the transaction does not hold the lock. The transaction stays open;
/// the caller decides whether to roll it back.
/// </summary>
public sealed class InquiryAppLockException : Exception
{
    /// <summary>Initializes the exception for a refused lock request.</summary>
    /// <param name="resource">The lock resource name.</param>
    /// <param name="failure">The outcome <c>sp_getapplock</c> reported.</param>
    public InquiryAppLockException(string resource, InquiryAppLockFailure failure)
        : base(CreateMessage(resource, failure))
    {
        Resource = resource;
        Failure = failure;
    }

    /// <summary>Gets the lock resource name.</summary>
    public string Resource { get; }

    /// <summary>Gets the outcome <c>sp_getapplock</c> reported.</summary>
    public InquiryAppLockFailure Failure { get; }

    /// <summary>Gets the raw <c>sp_getapplock</c> return code.</summary>
    public int ReturnCode => (int)Failure;

    private static string CreateMessage(string resource, InquiryAppLockFailure failure)
    {
        var reason = failure switch
        {
            InquiryAppLockFailure.Timeout => "the lock was not granted within the timeout",
            InquiryAppLockFailure.Canceled => "SQL Server canceled the lock request",
            InquiryAppLockFailure.Deadlock => "the lock request was chosen as a deadlock victim",
            InquiryAppLockFailure.InvalidRequest => "SQL Server rejected the request as invalid",
            _ => "sp_getapplock returned an undocumented result",
        };
        return $"Application lock '{resource}' was not acquired: {reason} (sp_getapplock returned {(int)failure}).";
    }
}
