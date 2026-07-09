using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Inquiry.Entities;
using Inquiry.Stores;

namespace Inquiry.FeatureCatalog;

[InquiryTable("JsonPathDoc")]
public sealed class JsonPathDoc
{
    [InquiryKey(IsGenerated = true)]
    public long Id { get; set; }

    [InquiryColumn("Name")]
    public string Name { get; set; } = string.Empty;

    [InquiryColumn("Data")]
    public string Data { get; set; } = string.Empty;
}

public partial class JsonPathDocStore : InquiryStore<JsonPathDoc>
{
    [InquiryInsert(ReturnEntity = true)]
    public partial Task<JsonPathDoc?> InsertAsync(JsonPathDoc doc, CancellationToken cancellationToken = default);

    [InquirySelectAllByPredicate]
    [InquiryWhere("Data", Compare.Equal, JsonPath = "$.status")]
    public partial Task<IReadOnlyList<JsonPathDoc>> ByStatusAsync(string status, CancellationToken cancellationToken = default);

    [InquirySelectAllByPredicate]
    [InquiryWhere("Data", Compare.Equal, JsonPath = "$.address.city")]
    public partial Task<IReadOnlyList<JsonPathDoc>> ByCityAsync(string city, CancellationToken cancellationToken = default);

    [InquirySelectAllByPredicate]
    [InquiryWhere("Name", Compare.Like)]
    [InquiryWhere("Data", Compare.Equal, JsonPath = "$.status")]
    public partial Task<IReadOnlyList<JsonPathDoc>> ByNameAndStatusAsync(string name, string status, CancellationToken cancellationToken = default);
}
