using Inquiry.Stores;

namespace Fulfillment;

public partial class WorkItemStore : InquiryStore<WorkItem>
{
    [InquiryInsert]
    public partial Task<int> InsertAsync(WorkItem item, CancellationToken ct = default);

    [InquirySelectOneByKey(LockMode = InquiryLockMode.Update)]
    public partial Task<WorkItem?> GetForUpdateAsync(long id, CancellationToken ct = default);

    [InquirySelectAllByPredicate(LockMode = InquiryLockMode.UpdateSkipLocked)]
    [InquiryWhere("QueueId", Parameter = nameof(queueId))]
    [InquiryWhere("State", Parameter = nameof(state))]
    public partial Task<IReadOnlyList<WorkItem>> AvailableAsync(
        int queueId, int state, CancellationToken ct = default);

    [InquiryUpdate]
    public partial Task<bool> UpdateAsync(WorkItem item, CancellationToken ct = default);
}
