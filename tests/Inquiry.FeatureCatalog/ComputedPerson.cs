using System.Threading;
using System.Threading.Tasks;
using Inquiry.Entities;
using Inquiry.Stores;

namespace Inquiry.FeatureCatalog;

[InquiryTable("ComputedPerson")]
public sealed class ComputedPerson
{
    [InquiryKey(IsGenerated = true)]
    public long Id { get; set; }

    [InquiryColumn("FirstName")]
    public string FirstName { get; set; } = string.Empty;

    [InquiryColumn("LastName")]
    public string LastName { get; set; } = string.Empty;

    [InquiryColumn("FullName", Computed = "FirstName || ' ' || LastName")]
    public string FullName { get; set; } = string.Empty;
}

public partial class ComputedPersonStore : InquiryStore<ComputedPerson>
{
    [InquiryInsert(ReturnEntity = true)]
    public partial Task<ComputedPerson?> InsertReturningAsync(ComputedPerson person, CancellationToken cancellationToken = default);

    [InquiryUpdate]
    public partial Task<bool> UpdateAsync(ComputedPerson person, CancellationToken cancellationToken = default);

    [InquirySelectOneByKey]
    public partial Task<ComputedPerson?> SelectByKeyAsync(long id, CancellationToken cancellationToken = default);
}
