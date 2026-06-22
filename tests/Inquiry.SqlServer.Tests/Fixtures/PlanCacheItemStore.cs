using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Inquiry.Stores;

namespace Inquiry.SqlServer.Tests.Fixtures;

public partial class PlanCacheItemStore : InquiryStore<PlanCacheItem>
{
    [InquiryInsert] public partial Task<int> InsertAsync(PlanCacheItem item, CancellationToken ct = default);
    [InquirySelectAllByField("Name")] public partial Task<IReadOnlyList<PlanCacheItem>> SelectByNameAsync(string name, CancellationToken ct = default);
}
