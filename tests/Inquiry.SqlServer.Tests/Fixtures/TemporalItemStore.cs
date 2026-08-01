using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Inquiry.Stores;

namespace Inquiry.SqlServer.Tests.Fixtures;

public partial class TemporalItemStore : InquiryStore<TemporalItem>
{
    [InquiryInsert] public partial Task<int> InsertAsync(TemporalItem item, CancellationToken ct = default);

    [InquirySelectAllByPredicate]
    [InquiryWhere("Moment", Compare.In)]
    public partial Task<IReadOnlyList<TemporalItem>> OnMomentsAsync(IReadOnlyList<DateTime> moments, CancellationToken ct = default);
}
