using Inquiry.Entities;
using Inquiry.Stores;

namespace Inquiry.Tests.UpdateReturning;

[InquiryTable("ReturningCounter")]
public sealed class ReturningCounter
{
    [InquiryKey]
    public int Id { get; set; }

    [InquiryColumn(Length = 50)]
    public string Category { get; set; } = string.Empty;

    [InquiryColumn]
    public int Hits { get; set; }
}

[InquiryProjection(typeof(ReturningCounter))]
public sealed class ReturningCounterHits
{
    [InquiryColumn]
    public int Id { get; set; }

    [InquiryColumn]
    public int Hits { get; set; }
}

public partial class ReturningCounterStore : InquiryStore<ReturningCounter>
{
    [InquiryInsert]
    public partial Task<int> InsertAsync(ReturningCounter counter, CancellationToken cancellationToken = default);

    [InquirySelectAll]
    public partial Task<IReadOnlyList<ReturningCounter>> SelectAllAsync(CancellationToken cancellationToken = default);

    [InquiryUpdate]
    [InquirySet(nameof(ReturningCounter.Hits), "{Hits} + 1")]
    [InquiryWhere(nameof(ReturningCounter.Id))]
    public partial Task<int?> IncrementAsync(int id, CancellationToken cancellationToken = default);

    [InquiryUpdate]
    [InquirySet(nameof(ReturningCounter.Hits), "{Hits} + 1")]
    [InquiryWhere(nameof(ReturningCounter.Category))]
    public partial Task<IReadOnlyList<int>> IncrementCategoryAsync(string category, CancellationToken cancellationToken = default);

    [InquiryUpdate]
    [InquirySet(nameof(ReturningCounter.Hits), "{Hits} + 10")]
    [InquiryWhere(nameof(ReturningCounter.Category))]
    public partial Task<IReadOnlyList<ReturningCounterHits>> BoostCategoryAsync(string category, CancellationToken cancellationToken = default);

    // The SET column is also the filter column, so the returned rows must show the new value.
    [InquiryUpdate]
    [InquiryWhere(nameof(ReturningCounter.Category))]
    public partial Task<IReadOnlyList<ReturningCounter>> RecategorizeAsync(string category, string fromCategory, CancellationToken cancellationToken = default);

    [InquiryUpdate]
    [InquiryWhere(nameof(ReturningCounter.Id))]
    public partial Task<ReturningCounter?> MoveAsync(string category, int id, CancellationToken cancellationToken = default);
}

/// <summary>
/// Shared live checks for #615. A set-based update returns the values as they are after the update:
/// one value or row for a single-row target, and one entry per updated row for a list.
/// </summary>
internal static class UpdateReturningScenarios
{
    /// <summary>Seeds ids 1-2 in category "a" and id 3 in category "b", each with 5 hits.</summary>
    private static async Task SeedAsync(ReturningCounterStore store)
    {
        await store.InsertAsync(new ReturningCounter { Id = 1, Category = "a", Hits = 5 });
        await store.InsertAsync(new ReturningCounter { Id = 2, Category = "a", Hits = 5 });
        await store.InsertAsync(new ReturningCounter { Id = 3, Category = "b", Hits = 5 });
    }

    internal static async Task AssertSingleValueReturnsTheNewValueOrNullAsync(ReturningCounterStore store)
    {
        await SeedAsync(store);

        Assert.Equal(6, await store.IncrementAsync(1));
        Assert.Equal(7, await store.IncrementAsync(1));
        Assert.Null(await store.IncrementAsync(99));

        var rows = await store.SelectAllAsync();
        Assert.Equal([7, 5, 5], rows.OrderBy(static row => row.Id).Select(static row => row.Hits));
    }

    internal static async Task AssertListReturnsOneEntryPerUpdatedRowAsync(ReturningCounterStore store)
    {
        await SeedAsync(store);

        Assert.Equal([6, 6], await store.IncrementCategoryAsync("a"));
        Assert.Empty(await store.IncrementCategoryAsync("missing"));

        var boosted = await store.BoostCategoryAsync("a");
        Assert.Equal([(1, 16), (2, 16)], boosted.OrderBy(static row => row.Id).Select(static row => (row.Id, row.Hits)));

        var rows = await store.SelectAllAsync();
        Assert.Equal([16, 16, 5], rows.OrderBy(static row => row.Id).Select(static row => row.Hits));
    }

    internal static async Task AssertRowsShowAChangedFilterColumnAsync(ReturningCounterStore store)
    {
        await SeedAsync(store);

        var moved = await store.RecategorizeAsync("c", "a");
        Assert.Equal([(1, "c"), (2, "c")], moved.OrderBy(static row => row.Id).Select(static row => (row.Id, row.Category)));

        var single = await store.MoveAsync("d", 3);
        Assert.NotNull(single);
        Assert.Equal(("d", 5), (single.Category, single.Hits));
        Assert.Null(await store.MoveAsync("d", 99));
    }
}
