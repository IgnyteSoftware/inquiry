using Inquiry.Stores;

namespace Inquiry.FeatureCatalog.LockingRegression;

/// <summary>
/// Offset-paged locking selects (#538). Oracle rejects these with INQ039 (ORA-02014), so only the
/// PostgreSQL, MySQL, MariaDB and SQL Server test projects compile this folder.
/// </summary>
public partial class LockProbePagedStore : InquiryStore<LockProbe>
{
    [InquirySelectAll(OrderBy = "Id ASC", Paged = true, LockMode = InquiryLockMode.Update)]
    public partial Task<IReadOnlyList<LockProbeSummary>> PageForUpdateAsync(int offset, int limit, CancellationToken cancellationToken = default);

    [InquirySelectAll(OrderBy = "Id ASC", Paged = true, LockMode = InquiryLockMode.UpdateNoWait)]
    public partial Task<IReadOnlyList<LockProbeSummary>> PageForUpdateNoWaitAsync(int offset, int limit, CancellationToken cancellationToken = default);

    [InquirySelectAll(OrderBy = "Id ASC", Paged = true, LockMode = InquiryLockMode.UpdateSkipLocked)]
    public partial Task<IReadOnlyList<LockProbeSummary>> PageForUpdateSkipLockedAsync(int offset, int limit, CancellationToken cancellationToken = default);

    [InquirySelectAll(OrderBy = "Id ASC", Paged = true, LockMode = InquiryLockMode.Share)]
    public partial Task<IReadOnlyList<LockProbeSummary>> PageForShareAsync(int offset, int limit, CancellationToken cancellationToken = default);
}
