using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Inquiry.Entities;
using Inquiry.Stores;

namespace Inquiry.FeatureCatalog;

[InquiryTable("GlobalFilterDoc")]
public sealed class GlobalFilterDoc
{
    [InquiryKey(IsGenerated = true)]
    public long Id { get; set; }

    [InquiryColumn("Name")]
    public string Name { get; set; } = string.Empty;

    [InquiryColumn("IsPublished"), InquiryGlobalFilter(Name = "PublishGate")]
    public bool IsPublished { get; set; }

    [InquiryColumn("IsDeleted"), InquirySoftDelete]
    public bool IsDeleted { get; set; }
}

public partial class GlobalFilterDocStore : InquiryStore<GlobalFilterDoc>
{
    [InquiryInsert]
    public partial Task<GlobalFilterDoc?> InsertAsync(GlobalFilterDoc doc, CancellationToken cancellationToken = default);

    [InquirySelectAll]
    public partial Task<IReadOnlyList<GlobalFilterDoc>> AllAsync(CancellationToken cancellationToken = default);

    [InquirySelectAll(IncludeDeleted = true)]
    public partial Task<IReadOnlyList<GlobalFilterDoc>> AllIncludingDeletedAsync(CancellationToken cancellationToken = default);

    [InquiryCount]
    public partial Task<long> CountPublishedAsync(CancellationToken cancellationToken = default);

    // Named-filter bypass (#82 phase A): drops only the PublishGate predicate from this method's
    // const — the soft-delete term stays, so a deleted draft remains hidden even here.
    [InquirySelectAll]
    [InquiryIgnoreFilter("PublishGate")]
    public partial Task<IReadOnlyList<GlobalFilterDoc>> AllIncludingDraftsAsync(CancellationToken cancellationToken = default);

    [InquiryCount]
    [InquiryIgnoreFilter("PublishGate")]
    public partial Task<long> CountIncludingDraftsAsync(CancellationToken cancellationToken = default);
}

[InquiryTable("GlobalFilterTicket")]
public sealed class GlobalFilterTicket
{
    [InquiryKey(IsGenerated = true)]
    public long Id { get; set; }

    [InquiryColumn("Title")]
    public string Title { get; set; } = string.Empty;

    [InquiryColumn("IsArchived"), InquiryGlobalFilter(KeepWhen = false)]
    public bool IsArchived { get; set; }
}

public partial class GlobalFilterTicketStore : InquiryStore<GlobalFilterTicket>
{
    [InquiryInsert]
    public partial Task<GlobalFilterTicket?> InsertAsync(GlobalFilterTicket ticket, CancellationToken cancellationToken = default);

    [InquirySelectAll]
    public partial Task<IReadOnlyList<GlobalFilterTicket>> AllAsync(CancellationToken cancellationToken = default);
}
