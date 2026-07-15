namespace Inquiry.Stores;

/// <summary>
/// Specifies the row-level lock intent for a generated SELECT query. The generated SQL acquires
/// the requested lock within the current transaction; using a lock mode outside a transaction is
/// a provider-specific error at runtime.
/// </summary>
public enum InquiryLockMode
{
    /// <summary>No row locking (the default).</summary>
    None = 0,

    /// <summary>
    /// Acquires an exclusive row lock (<c>FOR UPDATE</c> / <c>WITH (UPDLOCK, ROWLOCK)</c>).
    /// Other transactions that attempt to read-for-update or write the same rows will block
    /// until the current transaction completes.
    /// </summary>
    Update = 1,

    /// <summary>
    /// Acquires an exclusive row lock and fails immediately if the rows are already locked
    /// (<c>FOR UPDATE NOWAIT</c>). Not supported on all providers.
    /// </summary>
    UpdateNoWait = 2,

    /// <summary>
    /// Acquires an exclusive row lock but silently skips rows that are already locked
    /// (<c>FOR UPDATE SKIP LOCKED</c>). Ideal for work-queue patterns. Not supported on
    /// all providers.
    /// </summary>
    UpdateSkipLocked = 3,

    /// <summary>
    /// Acquires a shared row lock (<c>FOR SHARE</c> / <c>LOCK IN SHARE MODE</c> /
    /// <c>WITH (HOLDLOCK, ROWLOCK)</c>). Other transactions can also acquire a shared lock but
    /// cannot write until the current transaction completes. Not supported on all providers.
    /// </summary>
    Share = 4,
}
