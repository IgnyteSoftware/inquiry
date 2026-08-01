using Inquiry.Stores;

namespace Inquiry.Sqlite.Tests.Fixtures;

public partial class ScheduleItemStore : InquiryStore<ScheduleItem>
{

    [InquiryInsert(ReturnEntity = true)]
    public partial Task<ScheduleItem?> InsertReturningAsync(ScheduleItem item, CancellationToken cancellationToken = default);

    [InquirySelectAll]
    public partial Task<IReadOnlyList<ScheduleItem>> SelectAllAsync(CancellationToken cancellationToken = default);
}
