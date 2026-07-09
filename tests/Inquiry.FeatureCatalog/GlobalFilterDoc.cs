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

    [InquiryColumn("IsPublished"), InquiryGlobalFilter]
    public bool IsPublished { get; set; }

    [InquiryColumn("IsDeleted"), InquirySoftDelete]
    public bool IsDeleted { get; set; }
}

public partial class GlobalFilterDocStore : InquiryStore<GlobalFilterDoc>
{
    [InquiryInsert(ReturnEntity = true)]
    public partial Task<GlobalFilterDoc?> InsertAsync(GlobalFilterDoc doc, CancellationToken cancellationToken = default);

    [InquirySelectAll]
    public partial Task<IReadOnlyList<GlobalFilterDoc>> AllAsync(CancellationToken cancellationToken = default);

    [InquirySelectAll(IncludeDeleted = true)]
    public partial Task<IReadOnlyList<GlobalFilterDoc>> AllIncludingDeletedAsync(CancellationToken cancellationToken = default);

    [InquiryCount]
    public partial Task<long> CountPublishedAsync(CancellationToken cancellationToken = default);
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
    [InquiryInsert(ReturnEntity = true)]
    public partial Task<GlobalFilterTicket?> InsertAsync(GlobalFilterTicket ticket, CancellationToken cancellationToken = default);

    [InquirySelectAll]
    public partial Task<IReadOnlyList<GlobalFilterTicket>> AllAsync(CancellationToken cancellationToken = default);
}
