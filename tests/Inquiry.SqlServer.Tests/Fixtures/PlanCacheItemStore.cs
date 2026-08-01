using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Inquiry.Stores;

namespace Inquiry.SqlServer.Tests.Fixtures;

public partial class PlanCacheItemStore : InquiryStore<PlanCacheItem>
{
    [InquiryInsert] public partial Task<int> InsertAsync(PlanCacheItem item, CancellationToken ct = default);
    [InquirySelectAllByField("Name")] public partial Task<IReadOnlyList<PlanCacheItem>> SelectByNameAsync(string name, CancellationToken ct = default);

    // Compare.In over the declared-length Name column: the IN-element parameters must carry Size = 64 too
    // (#102), or each value length renders a distinct sp_executesql signature like the scalar path did pre-#56.
    [InquirySelectAllByPredicate]
    [InquiryWhere("Name", Compare.In)]
    public partial Task<IReadOnlyList<PlanCacheItem>> InNamesAsync(IReadOnlyList<string> name, CancellationToken ct = default);
}
