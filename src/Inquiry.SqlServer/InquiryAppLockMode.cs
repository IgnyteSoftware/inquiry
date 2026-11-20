namespace Inquiry.SqlServer;

/// <summary>
/// The <c>@LockMode</c> that <see cref="SqlServerAppLockExtensions.AcquireAppLockAsync"/> passes to
/// <c>sp_getapplock</c>. The modes follow the SQL Server lock compatibility matrix.
/// </summary>
public enum InquiryAppLockMode
{
    /// <summary>Compatible with other <c>Shared</c>, <c>Update</c> and <c>IntentShared</c> holders.</summary>
    Shared = 0,

    /// <summary>Compatible with <c>Shared</c> and <c>IntentShared</c> holders only.</summary>
    Update = 1,

    /// <summary>Compatible with every mode except <c>Exclusive</c>.</summary>
    IntentShared = 2,

    /// <summary>Compatible with <c>IntentShared</c> and other <c>IntentExclusive</c> holders only.</summary>
    IntentExclusive = 3,

    /// <summary>Compatible with no other holder. Use it to let one transaction at a time run the protected work.</summary>
    Exclusive = 4,
}
