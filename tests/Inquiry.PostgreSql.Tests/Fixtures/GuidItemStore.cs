using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Inquiry.Stores;

namespace Inquiry.PostgreSql.Tests.Fixtures;

public partial class GuidItemStore : InquiryStore<GuidItem>
{
    [InquiryUpsert] public partial Task<int> UpsertAsync(GuidItem item, CancellationToken ct = default);
    [InquiryUpsert(ReturnEntity = true)] public partial Task<GuidItem?> UpsertReturningAsync(GuidItem item, CancellationToken ct = default);
    [InquirySelectOneByKey] public partial Task<GuidItem?> SelectByKeyAsync(Guid? id, CancellationToken ct = default);
    [InquirySelectAll] public partial Task<IReadOnlyList<GuidItem>> SelectAllAsync(CancellationToken ct = default);
}
