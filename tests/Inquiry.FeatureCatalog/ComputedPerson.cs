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
    [InquiryComputedExpression("postgresql", "\"FirstName\" || ' ' || \"LastName\"")]
    [InquiryComputedExpression("mysql", "CONCAT(FirstName, ' ', LastName)")]
    [InquiryComputedExpression("mariadb", "CONCAT(FirstName, ' ', LastName)")]
    public string FullName { get; set; } = string.Empty;

    [InquiryColumn("Base Value")]
    public int BaseValue { get; set; }

    [InquiryColumn("MixedCaseValue")]
    public int MixedCaseValue { get; set; }

    [InquiryColumn("Computed Total", Computed = "\"Base Value\" + \"MixedCaseValue\"")]
    [InquiryComputedExpression("sqlserver", "[Base Value] + [MixedCaseValue]")]
    [InquiryComputedExpression("mysql", "`Base Value` + `MixedCaseValue`")]
    [InquiryComputedExpression("mariadb", "`Base Value` + `MixedCaseValue`")]
    [InquiryComputedExpression("oracle", "\"Base Value\" + MixedCaseValue")]
    public int ComputedTotal { get; set; }
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
