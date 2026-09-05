using System.Threading;
using System.Threading.Tasks;
using Inquiry.Entities;
using Inquiry.Stores;

namespace Inquiry.FeatureCatalog;

[InquiryTable("AuditUserDoc")]
public sealed class AuditUserDoc
{
    [InquiryKey(IsGenerated = true)]
    public long Id { get; set; }

    [InquiryColumn]
    public string Title { get; set; } = string.Empty;

    [InquiryCreatedBy]
    public string? CreatedBy { get; set; }

    [InquiryModifiedBy]
    public string? ModifiedBy { get; set; }
}

public partial class AuditUserDocStore : InquiryStore<AuditUserDoc>
{
    [InquiryInsert]
    public partial Task<AuditUserDoc?> InsertReturningAsync(AuditUserDoc doc, CancellationToken cancellationToken = default);

    [InquiryUpdate]
    public partial Task<bool> UpdateAsync(AuditUserDoc doc, CancellationToken cancellationToken = default);

    [InquirySelectOneByKey]
    public partial Task<AuditUserDoc?> SelectByKeyAsync(long id, CancellationToken cancellationToken = default);
}
