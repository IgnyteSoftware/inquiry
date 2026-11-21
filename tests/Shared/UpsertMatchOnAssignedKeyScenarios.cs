using Inquiry.Entities;
using Inquiry.Stores;

namespace Inquiry.Tests.UpsertMatchOn;

// MySQL and MariaDB reject a MatchOn upsert whose key is not AUTO_INCREMENT (INQ121), so only the
// other providers link this file.
[InquiryTable("MatchTag")]
public sealed class MatchTag
{
    [InquiryKey(Length = 36)]
    public string Id { get; set; } = string.Empty;

    [InquiryColumn(Length = 20, IsUnique = true)]
    public string Code { get; set; } = string.Empty;

    [InquiryColumn(Length = 50)]
    public string Label { get; set; } = string.Empty;
}

public partial class MatchTagStore : InquiryStore<MatchTag>
{
    [InquiryUpsert(MatchOn = new[] { nameof(MatchTag.Code) })]
    public partial Task<MatchTag?> UpsertReturningAsync(MatchTag tag, CancellationToken cancellationToken = default);

    [InquirySelectAll(OrderBy = "Id ASC")]
    public partial Task<IReadOnlyList<MatchTag>> SelectAllAsync(CancellationToken cancellationToken = default);
}

/// <summary>#614: a MatchOn upsert keeps the assigned key of the row it updates.</summary>
internal static class UpsertMatchOnAssignedKeyScenarios
{
    internal static async Task AssignedKeyOfExistingRowDoesNotChangeAsync(MatchTagStore store)
    {
        var inserted = await store.UpsertReturningAsync(new MatchTag { Id = "tag-a", Code = "red", Label = "one" });
        Assert.NotNull(inserted);
        Assert.Equal("tag-a", inserted.Id);

        var updated = await store.UpsertReturningAsync(new MatchTag { Id = "tag-b", Code = "red", Label = "two" });
        Assert.NotNull(updated);
        Assert.Equal("tag-a", updated.Id);
        Assert.Equal("two", updated.Label);

        var row = Assert.Single(await store.SelectAllAsync());
        Assert.Equal("tag-a", row.Id);
        Assert.Equal("two", row.Label);
    }
}
