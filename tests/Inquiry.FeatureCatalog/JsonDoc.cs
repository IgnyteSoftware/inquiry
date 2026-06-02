using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Inquiry.Entities;
using Inquiry.Stores;

namespace Inquiry.FeatureCatalog;

/// <summary>A money value object stored through a custom <see cref="IInquiryValueConverter{TModel,TProvider}"/>.</summary>
public readonly struct Money
{
    public decimal Amount { get; init; }
}

public sealed class MoneyConverter : IInquiryValueConverter<Money, decimal>
{
    public decimal ToProvider(Money model) => model.Amount;
    public Money FromProvider(decimal provider) => new() { Amount = provider };
}

/// <summary>
/// W10 fixture shared by every dialect test project: a value-converter column (<see cref="Balance"/>) and a
/// JSON column (<see cref="Tags"/>, serialized to text). Columns are kept text/numeric-compatible across
/// dialects so the test exercises Inquiry's converter/JSON serialization, not provider jsonb binding.
/// </summary>
[InquiryTable("JsonDoc")]
public sealed class JsonDoc
{
    [InquiryKey(IsGenerated = true)]
    public long Id { get; set; }

    [InquiryColumn("Owner")]
    public string Owner { get; set; } = string.Empty;

    [InquiryColumn("Balance", Converter = typeof(MoneyConverter))]
    public Money Balance { get; set; }

    [InquiryColumn("Tags"), InquiryJson]
    public List<string>? Tags { get; set; }
}

public partial class JsonDocStore : InquiryStore<JsonDoc>
{
    [InquiryInsert]
    public partial Task<int> InsertAsync(JsonDoc doc, CancellationToken cancellationToken = default);

    [InquirySelectOneByKey]
    public partial Task<JsonDoc?> GetAsync(long id, CancellationToken cancellationToken = default);
}
