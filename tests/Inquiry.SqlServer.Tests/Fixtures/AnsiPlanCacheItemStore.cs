using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Inquiry.Stores;

namespace Inquiry.SqlServer.Tests.Fixtures;

public partial class AnsiPlanCacheItemStore : InquiryStore<AnsiPlanCacheItem>
{
    [InquirySelectAllByField("Code")]
    public partial Task<IReadOnlyList<AnsiPlanCacheItem>> SelectByCodeAsync(string code, CancellationToken ct = default);
}
