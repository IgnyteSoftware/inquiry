using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Inquiry.Entities;
using Inquiry.Stores;

namespace Inquiry.FeatureCatalog;

[InquiryTable("AuditDoc")]
public sealed class AuditDoc
{
    [InquiryKey(IsGenerated = true)]
    public long Id { get; set; }

    [InquiryColumn]
    public string Title { get; set; } = string.Empty;

    [InquiryCreatedAt]
    public DateTime CreatedAt { get; set; }

    [InquiryModifiedAt]
    public DateTime ModifiedAt { get; set; }
}

public partial class AuditDocStore : InquiryStore<AuditDoc>
{
    [InquiryInsert]
    public partial Task<int> InsertAsync(AuditDoc doc, CancellationToken cancellationToken = default);

    [InquiryInsert(ReturnEntity = true)]
    public partial Task<AuditDoc?> InsertReturningAsync(AuditDoc doc, CancellationToken cancellationToken = default);

    [InquiryUpdate]
    public partial Task<bool> UpdateAsync(AuditDoc doc, CancellationToken cancellationToken = default);

    [InquiryUpdateAll]
    public partial Task<int> UpdateAllAsync(IEnumerable<AuditDoc> docs, CancellationToken cancellationToken = default);

    [InquirySelectOneByKey]
    public partial Task<AuditDoc?> SelectByKeyAsync(long id, CancellationToken cancellationToken = default);
}
