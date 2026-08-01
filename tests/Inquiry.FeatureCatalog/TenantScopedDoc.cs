using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Inquiry;
using Inquiry.Entities;
using Inquiry.Stores;

namespace Inquiry.FeatureCatalog;

/// <summary>
/// Runtime-parameterized global filter (#82 phase B): every read composes
/// <c>"TenantId" = @__gf_TenantId</c> with the value bound from the ambient
/// <see cref="InquiryFilterContext"/> at execute time. The filter is unnamed, so it can never be
/// bypassed; the unnamed constant-bool <c>IsActive</c> filter rides along to prove the two modes
/// compose. Writes carry no filter in this release, which is what lets the tests seed several
/// tenants without juggling scopes.
/// </summary>
[InquiryTable("TenantScopedDoc")]
public sealed class TenantScopedDoc
{
    [InquiryKey(IsGenerated = true)]
    public long Id { get; set; }

    [InquiryColumn("TenantId"), InquiryGlobalFilter(ContextKey = "TenantId")]
    public long TenantId { get; set; }

    [InquiryColumn("Title")]
    public string Title { get; set; } = string.Empty;

    [InquiryColumn("IsActive"), InquiryGlobalFilter]
    public bool IsActive { get; set; } = true;
}

public partial class TenantScopedDocStore : InquiryStore<TenantScopedDoc>
{
    [InquiryInsert]
    public partial Task<int> InsertAsync(TenantScopedDoc doc, CancellationToken cancellationToken = default);

    [InquirySelectAll]
    public partial Task<IReadOnlyList<TenantScopedDoc>> AllAsync(CancellationToken cancellationToken = default);

    [InquirySelectOneByKey]
    public partial Task<TenantScopedDoc?> ByKeyAsync(long id, CancellationToken cancellationToken = default);

    [InquirySelectAllByField("Title")]
    public partial Task<IReadOnlyList<TenantScopedDoc>> ByTitleAsync(string title, CancellationToken cancellationToken = default);

    [InquiryCount]
    public partial Task<long> CountAsync(CancellationToken cancellationToken = default);
}
