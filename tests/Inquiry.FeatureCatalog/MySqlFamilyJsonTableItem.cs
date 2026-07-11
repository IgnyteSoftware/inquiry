using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Inquiry.Entities;
using Inquiry.Stores;

namespace Inquiry.FeatureCatalog;

[InquiryTable("MySqlFamilyJsonTableItem")]
public sealed class MySqlFamilyJsonTableItem
{
    [InquiryKey(IsGenerated = true)] public int Id { get; set; }
    [InquiryColumn] public DateTime OccurredAt { get; set; }
    [InquiryColumn] public DateTimeOffset ObservedAt { get; set; }
    [InquiryColumn] public DateOnly Day { get; set; }
    [InquiryColumn] public TimeOnly Clock { get; set; }
    [InquiryColumn] public Guid CorrelationId { get; set; }
    [InquiryColumn] public byte[] Payload { get; set; } = Array.Empty<byte>();
    [InquiryColumn(Precision = 18, Scale = 4)] public decimal Amount { get; set; }
}

public partial class MySqlFamilyJsonTableItemStore : InquiryStore<MySqlFamilyJsonTableItem>
{
    [InquiryInsert(ReturnEntity = true)]
    public partial Task<MySqlFamilyJsonTableItem?> InsertAsync(MySqlFamilyJsonTableItem item, CancellationToken cancellationToken = default);

    [InquirySelectAllByPredicate, InquiryWhere("OccurredAt", Compare.In)]
    public partial Task<IReadOnlyList<MySqlFamilyJsonTableItem>> ByOccurredAt(IReadOnlyList<DateTime> values, CancellationToken cancellationToken = default);
    [InquirySelectAllByPredicate, InquiryWhere("ObservedAt", Compare.In)]
    public partial Task<IReadOnlyList<MySqlFamilyJsonTableItem>> ByObservedAt(IReadOnlyList<DateTimeOffset> values, CancellationToken cancellationToken = default);
    [InquirySelectAllByPredicate, InquiryWhere("Day", Compare.In)]
    public partial Task<IReadOnlyList<MySqlFamilyJsonTableItem>> ByDay(IReadOnlyList<DateOnly> values, CancellationToken cancellationToken = default);
    [InquirySelectAllByPredicate, InquiryWhere("Clock", Compare.In)]
    public partial Task<IReadOnlyList<MySqlFamilyJsonTableItem>> ByClock(IReadOnlyList<TimeOnly> values, CancellationToken cancellationToken = default);
    [InquirySelectAllByPredicate, InquiryWhere("CorrelationId", Compare.In)]
    public partial Task<IReadOnlyList<MySqlFamilyJsonTableItem>> ByCorrelationId(IReadOnlyList<Guid> values, CancellationToken cancellationToken = default);
    [InquirySelectAllByPredicate, InquiryWhere("Payload", Compare.In)]
    public partial Task<IReadOnlyList<MySqlFamilyJsonTableItem>> ByPayload(IReadOnlyList<byte[]> values, CancellationToken cancellationToken = default);
    [InquirySelectAllByPredicate, InquiryWhere("Amount", Compare.In)]
    public partial Task<IReadOnlyList<MySqlFamilyJsonTableItem>> ByAmount(IReadOnlyList<decimal> values, CancellationToken cancellationToken = default);
}
