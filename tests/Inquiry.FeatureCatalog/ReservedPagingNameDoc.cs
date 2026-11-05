using Inquiry.Entities;
using Inquiry.Stores;

namespace Inquiry.FeatureCatalog;

/// <summary>
/// Mapped properties and a JSON-path leaf whose names equal the default offset-paging bind names
/// (#543). Each paged method must bind its filter value and its offset and limit to separate
/// parameters. <c>__Limit</c> differs from <c>__limit</c> only by case, which most providers ignore.
/// </summary>
[InquiryTable("ReservedPagingNameDoc")]
public sealed class ReservedPagingNameDoc
{
    [InquiryKey(IsGenerated = true)]
    public long Id { get; set; }

    // The reserved-looking names are the fixture: they equal the paging bind names.
#pragma warning disable IDE1006
    [InquiryColumn("PageOffset")]
    public int __offset { get; set; }

    [InquiryColumn("PageLimit")]
    public int __Limit { get; set; }
#pragma warning restore IDE1006

    [InquiryColumn("Data")]
    public string Data { get; set; } = string.Empty;

    [InquiryColumn("Title")]
    public string Title { get; set; } = string.Empty;
}

public partial class ReservedPagingNameDocStore : InquiryStore<ReservedPagingNameDoc>
{
    [InquiryInsert]
    public partial Task<int> InsertAsync(ReservedPagingNameDoc doc, CancellationToken cancellationToken = default);

    [InquirySelectAllByField("__offset", OrderBy = "Id ASC", Paged = true)]
    public partial Task<IReadOnlyList<ReservedPagingNameDoc>> ByOffsetFieldAsync(int value, int offset, int limit, CancellationToken cancellationToken = default);

    [InquirySelectAllByField("__Limit", OrderBy = "Id ASC", Paged = true)]
    public partial Task<IReadOnlyList<ReservedPagingNameDoc>> ByLimitFieldAsync(int value, int offset, int limit, CancellationToken cancellationToken = default);

    [InquirySelectAllByPredicate(OrderBy = "Id ASC", Paged = true)]
    [InquiryWhere("__offset")]
    public partial Task<IReadOnlyList<ReservedPagingNameDoc>> WhereOffsetAsync(int value, int offset, int limit, CancellationToken cancellationToken = default);

    [InquirySelectAllByPredicate(OrderBy = "Id ASC", Paged = true)]
    [InquiryWhere("__Limit")]
    public partial Task<IReadOnlyList<ReservedPagingNameDoc>> WhereLimitAsync(int value, int offset, int limit, CancellationToken cancellationToken = default);

    [InquirySelectAllByPredicate(OrderBy = "Id ASC", Paged = true)]
    [InquiryWhere("Data", Compare.Equal, JsonPath = "$.__limit")]
    public partial Task<IReadOnlyList<ReservedPagingNameDoc>> WhereJsonLimitAsync(string value, int offset, int limit, CancellationToken cancellationToken = default);
}

/// <summary>
/// An entity with no reserved-looking property (#543). Only the JSON-path leaf <c>__limit</c> matches a
/// paging bind name, so the criterion name must move instead of the paging name.
/// </summary>
[InquiryTable("ReservedPagingJsonDoc")]
public sealed class ReservedPagingJsonDoc
{
    [InquiryKey(IsGenerated = true)]
    public long Id { get; set; }

    [InquiryColumn("Data")]
    public string Data { get; set; } = string.Empty;

    [InquiryColumn("Title")]
    public string Title { get; set; } = string.Empty;
}

public partial class ReservedPagingJsonDocStore : InquiryStore<ReservedPagingJsonDoc>
{
    [InquiryInsert]
    public partial Task<int> InsertAsync(ReservedPagingJsonDoc doc, CancellationToken cancellationToken = default);

    [InquirySelectAllByPredicate(OrderBy = "Id ASC", Paged = true)]
    [InquiryWhere("Data", Compare.Equal, JsonPath = "$.__limit")]
    public partial Task<IReadOnlyList<ReservedPagingJsonDoc>> WhereJsonLimitAsync(string value, int offset, int limit, CancellationToken cancellationToken = default);
}
