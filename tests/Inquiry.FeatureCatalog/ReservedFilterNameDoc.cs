using Inquiry.Entities;
using Inquiry.Stores;

namespace Inquiry.FeatureCatalog;

/// <summary>
/// A mapped property whose C# name equals the default bind name of the tenant filter (#288). The
/// generator must give the filter a different bind name, so a probe value never reaches the tenant
/// term and the tenant value never reaches the probe term.
/// </summary>
[InquiryTable("ReservedFilterNameDoc")]
public sealed class ReservedFilterNameDoc
{
    [InquiryKey(IsGenerated = true)]
    public long Id { get; set; }

    [InquiryColumn("TenantId"), InquiryGlobalFilter(ContextKey = "TenantId", EnforceOnWrites = true)]
    public long TenantId { get; set; }

    // The reserved-looking name is the fixture: it equals the filter's default bind name.
#pragma warning disable IDE1006
    [InquiryColumn("Probe")]
    public long __gf_TenantId { get; set; }
#pragma warning restore IDE1006

    [InquiryColumn("Title")]
    public string Title { get; set; } = string.Empty;
}

public partial class ReservedFilterNameDocStore : InquiryStore<ReservedFilterNameDoc>
{
    [InquiryInsert]
    public partial Task<int> InsertAsync(ReservedFilterNameDoc doc, CancellationToken cancellationToken = default);

    [InquirySelectAll]
    public partial Task<IReadOnlyList<ReservedFilterNameDoc>> AllAsync(CancellationToken cancellationToken = default);

    /// <summary>The argument binder runs before the filter binder on this shape.</summary>
    [InquirySelectAllByField("__gf_TenantId")]
    public partial Task<IReadOnlyList<ReservedFilterNameDoc>> ByProbeAsync(long probe, CancellationToken cancellationToken = default);

    /// <summary>The filter binder runs before the argument binder on this shape.</summary>
    [InquirySelectAllByPredicate]
    [InquiryWhere("__gf_TenantId")]
    public partial Task<IReadOnlyList<ReservedFilterNameDoc>> WhereProbeAsync(long probe, CancellationToken cancellationToken = default);

    [InquiryUpdate]
    public partial Task<bool> UpdateAsync(ReservedFilterNameDoc doc, CancellationToken cancellationToken = default);
}
