using Inquiry.Entities;
using Inquiry.Stores;

namespace Inquiry.FeatureCatalog;

public sealed class NativeJsonSettings
{
    public string Theme { get; set; } = "light";
    public NativeJsonAddress? Address { get; set; }
}

public sealed class NativeJsonAddress
{
    public string City { get; set; } = "";
}

/// <summary>
/// Fixture for the native JSON column opt-in: an <c>[InquiryJson]</c> property (<see cref="Settings"/>) and a
/// plain string property (<see cref="Payload"/>) both declared with <c>SqlType = "JSON"</c>. Each provider test
/// creates the table with its engine's native JSON type.
/// </summary>
[InquiryTable("NativeJsonDoc")]
public sealed class NativeJsonDoc
{
    [InquiryKey]
    public int Id { get; set; }

    [InquiryColumn(SqlType = "JSON"), InquiryJson]
    public NativeJsonSettings? Settings { get; set; }

    [InquiryColumn(SqlType = "JSON")]
    public string? Payload { get; set; }

    public static NativeJsonDoc Create(int id, string theme, string city)
        => new()
        {
            Id = id,
            Settings = new NativeJsonSettings { Theme = theme, Address = new NativeJsonAddress { City = city } },
            Payload = $$$"""{"Theme":"{{{theme}}}","Address":{"City":"{{{city}}}"}}""",
        };
}

public partial class NativeJsonDocStore : InquiryStore<NativeJsonDoc>
{
    [InquiryInsert]
    public partial Task<int> InsertAsync(NativeJsonDoc doc, CancellationToken cancellationToken = default);

    [InquiryInsert]
    public partial Task<int> InsertAllAsync(IEnumerable<NativeJsonDoc> docs, CancellationToken cancellationToken = default);

    [InquiryBulkInsert]
    public partial Task<long> BulkInsertAsync(IEnumerable<NativeJsonDoc> docs, CancellationToken cancellationToken = default);

    [InquiryUpdate]
    public partial Task<bool> UpdateAsync(NativeJsonDoc doc, CancellationToken cancellationToken = default);

    [InquiryUpdate]
    public partial Task<int> UpdateAllAsync(IEnumerable<NativeJsonDoc> docs, CancellationToken cancellationToken = default);

    [InquiryUpdate]
    [InquiryWhere(nameof(NativeJsonDoc.Id))]
    public partial Task<int> SetSettingsAsync(NativeJsonSettings? settings, int id, CancellationToken cancellationToken = default);

    [InquiryUpsert]
    public partial Task<int> UpsertAsync(NativeJsonDoc doc, CancellationToken cancellationToken = default);

    [InquirySelectOneByKey]
    public partial Task<NativeJsonDoc?> GetAsync(int id, CancellationToken cancellationToken = default);

    [InquirySelectAllByPredicate]
    [InquiryWhere(nameof(NativeJsonDoc.Payload), Compare.Equal, JsonPath = "$.Address.City")]
    public partial Task<IReadOnlyList<NativeJsonDoc>> ByPayloadCityAsync(string city, CancellationToken cancellationToken = default);
}
