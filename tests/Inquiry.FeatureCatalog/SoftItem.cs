using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Inquiry.Entities;
using Inquiry.Stores;

namespace Inquiry.FeatureCatalog;

/// <summary>
/// Soft-delete fixture shared by every dialect test project. <see cref="IsDeleted"/> is the
/// soft-delete flag: normal selects filter it out, <c>IncludeDeleted</c> opts back in, a delete flips it
/// (unless <c>HardDelete</c>), and restore clears it.
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

public partial class SoftItemStore : InquiryStore<SoftItem>
{
    [InquiryInsert(ReturnEntity = true)]
    public partial Task<SoftItem?> InsertAsync(SoftItem item, CancellationToken cancellationToken = default);

    [InquirySelectAll]
    public partial Task<IReadOnlyList<SoftItem>> AllAsync(CancellationToken cancellationToken = default);

    [InquirySelectAll(IncludeDeleted = true)]
    public partial Task<IReadOnlyList<SoftItem>> AllIncludingDeletedAsync(CancellationToken cancellationToken = default);

    [InquirySelectOneByKey]
    public partial Task<SoftItem?> ByIdAsync(long id, CancellationToken cancellationToken = default);

    [InquiryDeleteOneByKey]
    public partial Task<bool> SoftDeleteAsync(long id, CancellationToken cancellationToken = default);

    [InquiryDeleteOneByKey(HardDelete = true)]
    public partial Task<bool> PurgeAsync(long id, CancellationToken cancellationToken = default);

    [InquiryRestoreOneByKey]
    public partial Task<bool> RestoreAsync(long id, CancellationToken cancellationToken = default);

    [InquiryCount]
    public partial Task<long> CountActiveAsync(CancellationToken cancellationToken = default);
}
