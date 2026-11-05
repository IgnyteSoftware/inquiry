using Inquiry.Stores;

namespace Inquiry.FeatureCatalog.LockingRegression;

/// <summary>
/// DISTINCT locking selects, with and without paging (#538). PostgreSQL and Oracle reject DISTINCT with
/// a lock (INQ039), so only the MySQL, MariaDB and SQL Server test projects compile this folder.
/// </summary>
public partial class LockProbeDistinctStore : InquiryStore<LockProbe>
{
    [InquirySelectAll(Distinct = true, LockMode = InquiryLockMode.Update)]
    public partial Task<IReadOnlyList<LockProbeState>> StatesForUpdateAsync(CancellationToken cancellationToken = default);

    [InquirySelectAll(Distinct = true, LockMode = InquiryLockMode.UpdateNoWait)]
    public partial Task<IReadOnlyList<LockProbeState>> StatesForUpdateNoWaitAsync(CancellationToken cancellationToken = default);

    [InquirySelectAll(Distinct = true, LockMode = InquiryLockMode.UpdateSkipLocked)]
    public partial Task<IReadOnlyList<LockProbeState>> StatesForUpdateSkipLockedAsync(CancellationToken cancellationToken = default);

    [InquirySelectAll(Distinct = true, LockMode = InquiryLockMode.Share)]
    public partial Task<IReadOnlyList<LockProbeState>> StatesForShareAsync(CancellationToken cancellationToken = default);

    [InquirySelectAll(OrderBy = "State ASC", Paged = true, Distinct = true, LockMode = InquiryLockMode.Update)]
    public partial Task<IReadOnlyList<LockProbeState>> StatesPageForUpdateAsync(int offset, int limit, CancellationToken cancellationToken = default);

    [InquirySelectAll(OrderBy = "State ASC", Paged = true, Distinct = true, LockMode = InquiryLockMode.UpdateNoWait)]
    public partial Task<IReadOnlyList<LockProbeState>> StatesPageForUpdateNoWaitAsync(int offset, int limit, CancellationToken cancellationToken = default);

    [InquirySelectAll(OrderBy = "State ASC", Paged = true, Distinct = true, LockMode = InquiryLockMode.UpdateSkipLocked)]
    public partial Task<IReadOnlyList<LockProbeState>> StatesPageForUpdateSkipLockedAsync(int offset, int limit, CancellationToken cancellationToken = default);

    [InquirySelectAll(OrderBy = "State ASC", Paged = true, Distinct = true, LockMode = InquiryLockMode.Share)]
    public partial Task<IReadOnlyList<LockProbeState>> StatesPageForShareAsync(int offset, int limit, CancellationToken cancellationToken = default);
}
