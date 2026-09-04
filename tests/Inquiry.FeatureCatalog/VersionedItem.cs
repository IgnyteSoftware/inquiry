using System.Threading;
using System.Threading.Tasks;
using Inquiry.Entities;
using Inquiry.Stores;

namespace Inquiry.FeatureCatalog;

/// <summary>
/// Optimistic-concurrency fixture shared by every dialect test project. The
/// <see cref="Version"/> column is an ORM-managed concurrency token: the generator bumps it on update
/// and folds the prior value into the WHERE so a stale write is a no-op.
/// </summary>
[InquiryTable("VersionedItem")]
public sealed class VersionedItem
{
    [InquiryKey(IsGenerated = true)]
    public long Id { get; set; }

    [InquiryColumn("Title")]
    public string Title { get; set; } = string.Empty;

    [InquiryConcurrencyToken]
    public int Version { get; set; }
}

public partial class VersionedItemStore : InquiryStore<VersionedItem>
{
    [InquiryInsert(ReturnEntity = true)]
    public partial Task<VersionedItem?> InsertAsync(VersionedItem item, CancellationToken cancellationToken = default);

    [InquirySelectOneByKey]
    public partial Task<VersionedItem?> ByIdAsync(long id, CancellationToken cancellationToken = default);

    [InquiryUpdate]
    public partial Task<bool> UpdateAsync(VersionedItem item, CancellationToken cancellationToken = default);

    [InquiryUpdate(ReturnEntity = true)]
    public partial Task<VersionedItem?> UpdateReturningAsync(VersionedItem item, CancellationToken cancellationToken = default);

    [InquiryDelete]
    public partial Task<bool> DeleteAsync(VersionedItem item, CancellationToken cancellationToken = default);
}
