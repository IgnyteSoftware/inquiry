namespace Inquiry.SqlServer;

/// <summary>
/// Why <c>sp_getapplock</c> did not grant an application lock. Each value equals the negative
/// return code that <c>sp_getapplock</c> reports for that outcome.
/// </summary>
public enum SqlServerAppLockFailure
{
    /// <summary>Return code -1: the lock was not granted within the timeout.</summary>
    Timeout = -1,

    /// <summary>Return code -2: SQL Server canceled the lock request.</summary>
    Canceled = -2,

    /// <summary>Return code -3: SQL Server chose the lock request as a deadlock victim.</summary>
    Deadlock = -3,

    /// <summary>Return code -999: SQL Server rejected a parameter or the call itself.</summary>
    InvalidRequest = -999,
}
