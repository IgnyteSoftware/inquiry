using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Inquiry.Entities;
using Inquiry.Stores;

namespace Inquiry.FeatureCatalog;

/// <summary>
/// Soft-delete fixture shared by every dialect test project. <see cref="IsDeleted"/> is the
/// soft-delete flag: normal selects filter it out, <c>IncludeDeleted</c> opts back in, a delete flips it
/// (unless the method uses <c>[InquiryHardDelete]</c>), and restore clears it.
/// </summary>
[InquiryTable("SoftItem")]
public sealed class SoftItem
{
    [InquiryKey(IsGenerated = true)]
    public long Id { get; set; }

    [InquiryColumn("Name")]
    public string Name { get; set; } = string.Empty;

    [InquiryColumn("IsDeleted"), InquirySoftDelete]
    public bool IsDeleted { get; set; }
}

/// <summary>
/// A projection over the soft-delete <see cref="SoftItem"/>. Projections select a subset of columns
/// and don't carry the soft-delete indicator, but the generated SELECT still AND-composes the entity's
/// soft-delete filter (audit P3 #14) so a projection hides soft-deleted rows just like the entity select.
/// </summary>
[InquiryProjection(typeof(SoftItem))]
public sealed record SoftItemName
{
    [InquiryColumn("Id")]
    public long Id { get; init; }

    [InquiryColumn("Name")]
    public string Name { get; init; } = string.Empty;
}

public partial class SoftItemStore : InquiryStore<SoftItem>
{
    [InquiryInsert]
    public partial Task<SoftItem?> InsertAsync(SoftItem item, CancellationToken cancellationToken = default);

    [InquirySelectAll]
    public partial Task<IReadOnlyList<SoftItem>> AllAsync(CancellationToken cancellationToken = default);

    [InquirySelectAll(IncludeDeleted = true)]
    public partial Task<IReadOnlyList<SoftItem>> AllIncludingDeletedAsync(CancellationToken cancellationToken = default);

    // Projection variants (audit P3 #14): NamesAsync hides soft-deleted rows; the IncludeDeleted form sees them.
    [InquirySelectAll]
    public partial Task<IReadOnlyList<SoftItemName>> NamesAsync(CancellationToken cancellationToken = default);

    [InquirySelectAll(IncludeDeleted = true)]
    public partial Task<IReadOnlyList<SoftItemName>> NamesIncludingDeletedAsync(CancellationToken cancellationToken = default);

    [InquirySelectOneByKey]
    public partial Task<SoftItem?> ByIdAsync(long id, CancellationToken cancellationToken = default);

    [InquiryDelete]
    public partial Task<bool> SoftDeleteAsync(long id, CancellationToken cancellationToken = default);

    [InquiryHardDelete]
    public partial Task<bool> PurgeAsync(long id, CancellationToken cancellationToken = default);

    [InquiryRestoreOneByKey]
    public partial Task<bool> RestoreAsync(long id, CancellationToken cancellationToken = default);

    [InquiryCount]
    public partial Task<long> CountActiveAsync(CancellationToken cancellationToken = default);
}
