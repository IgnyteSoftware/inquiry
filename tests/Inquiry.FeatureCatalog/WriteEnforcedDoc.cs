using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Inquiry;
using Inquiry.Entities;
using Inquiry.Stores;

namespace Inquiry.FeatureCatalog;

/// <summary>
/// Write-enforced runtime-parameterized global filter (#82 phase C): the tenant term composes onto
/// key-based WRITES as well as reads, so an update/delete/restore aimed at another tenant's row
/// affects zero rows instead of succeeding on a key the caller happened to learn. Soft delete rides
/// along so the hard-delete and restore paths can be exercised too. Insert stays unfiltered — the
/// entity carries its own TenantId — which is what lets tests seed several tenants without scopes.
/// </summary>
[InquiryTable("WriteEnforcedDoc")]
public sealed class WriteEnforcedDoc
{
    [InquiryKey(IsGenerated = true)]
    public long Id { get; set; }

    [InquiryColumn("TenantId"), InquiryGlobalFilter(ContextKey = "TenantId", EnforceOnWrites = true)]
    public long TenantId { get; set; }

    [InquiryColumn("Title")]
    public string Title { get; set; } = string.Empty;

    [InquiryColumn("IsDeleted"), InquirySoftDelete]
    public bool IsDeleted { get; set; }
}

public partial class WriteEnforcedDocStore : InquiryStore<WriteEnforcedDoc>
{
    [InquiryInsert]
    public partial Task<int> InsertAsync(WriteEnforcedDoc doc, CancellationToken cancellationToken = default);

    /// <summary>Reads soft-deleted rows too, so a test can prove a blocked delete left the row alone.</summary>
    [InquirySelectAll(IncludeDeleted = true)]
    public partial Task<IReadOnlyList<WriteEnforcedDoc>> AllAsync(CancellationToken cancellationToken = default);

    [InquiryUpdate]
    public partial Task<bool> UpdateAsync(WriteEnforcedDoc doc, CancellationToken cancellationToken = default);

    [InquiryUpdate(ReturnEntity = true)]
    public partial Task<WriteEnforcedDoc?> UpdateReturningAsync(WriteEnforcedDoc doc, CancellationToken cancellationToken = default);

    [InquiryDelete]
    public partial Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default);

    [InquiryDelete(HardDelete = true)]
    public partial Task<bool> PurgeAsync(long id, CancellationToken cancellationToken = default);

    [InquiryRestoreOneByKey]
    public partial Task<bool> RestoreAsync(long id, CancellationToken cancellationToken = default);

    [InquiryDelete, InquiryWhere("Id", Compare.In)]
    public partial Task<int> DeleteAllAsync(IEnumerable<long> ids, CancellationToken cancellationToken = default);

    /// <summary>
    /// Exercises the set-based UpdateAll route on the MySQL family, whose footer is a multi-table
    /// <c>UPDATE … JOIN</c> where the enforced term has to be qualified with the target alias. Needs
    /// two or more distinct keys per call before the runtime picks that route over the row route.
    /// </summary>
    [InquiryUpdateAll]
    public partial Task<int> UpdateAllAsync(IEnumerable<WriteEnforcedDoc> docs, CancellationToken cancellationToken = default);

    /// <summary>
    /// A HARD set-based delete: it drops the soft-delete activeness term but must still carry the
    /// enforced tenant term, which is the one predicate-write shape that composed nothing before.
    /// </summary>
    [InquiryDelete(HardDelete = true)]
    [InquiryWhere("Title")]
    public partial Task<int> PurgeByTitleAsync(string title, CancellationToken cancellationToken = default);
}
