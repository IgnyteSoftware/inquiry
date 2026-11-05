using Fulfillment;
using Inquiry.Stores;

namespace Inquiry.FeatureCatalog.Locking;

/// <summary>
/// The <see cref="InquiryLockMode.Share"/> probe. Oracle rejects Share with INQ039, so only the
/// PostgreSQL, MySQL, MariaDB and SQL Server test projects compile this file.
/// </summary>
public partial class WorkItemShareStore : InquiryStore<WorkItem>
{
    [InquirySelectOneByKey(LockMode = InquiryLockMode.Share)]
    public partial Task<WorkItem?> GetForShareAsync(long id, CancellationToken cancellationToken = default);
}
