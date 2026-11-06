using Inquiry.Entities;
using Inquiry.Paging;
using Inquiry.Stores;
using Inquiry.Testing;

namespace Inquiry.Tests.InLists;

[InquiryTable("SmallInItem")]
public sealed class SmallInItem
{
    [InquiryKey]
    public int Id { get; set; }

    [InquiryColumn]
    public int? CategoryId { get; set; }

    [InquiryColumn(Length = 20)]
    public string? Code { get; set; }
}

public partial class SmallInItemStore : InquiryStore<SmallInItem>
{
    [InquiryInsert]
    public partial Task<int> InsertAsync(SmallInItem item, CancellationToken cancellationToken = default);

    [InquirySelectAllByPredicate(OrderBy = "Id ASC")]
    [InquiryWhere("CategoryId", Compare.In)]
    public partial Task<IReadOnlyList<SmallInItem>> InCategoriesAsync(IReadOnlyList<int> categoryIds, CancellationToken cancellationToken = default);

    [InquirySelectAllByPredicate(OrderBy = "Id ASC")]
    [InquiryWhere("CategoryId", Compare.In, Not = true)]
    public partial Task<IReadOnlyList<SmallInItem>> OutsideCategoriesAsync(IReadOnlyList<int> categoryIds, CancellationToken cancellationToken = default);

    [InquirySelectAllByPredicate(OrderBy = "Id ASC")]
    [InquiryWhere("CategoryId", Compare.In, Parameter = nameof(categoryIds))]
    [InquiryWhere("Id", Compare.In, Parameter = nameof(ids))]
    public partial Task<IReadOnlyList<SmallInItem>> InCategoriesAndIdsAsync(IReadOnlyList<int> categoryIds, IReadOnlyList<int> ids, CancellationToken cancellationToken = default);

    [InquirySelectAllByPredicate(OrderBy = "Id ASC", Paged = true)]
    [InquiryWhere("CategoryId", Compare.In)]
    public partial Task<InquiryPagedResult<SmallInItem>> PageInCategoriesAsync(IReadOnlyList<int> categoryIds, int offset, int limit, CancellationToken cancellationToken = default);

    [InquirySelectAllByPredicate(OrderBy = "Id ASC")]
    [InquiryWhere("Code", Compare.In)]
    public partial Task<IReadOnlyList<SmallInItem>> InCodesAsync(IReadOnlyList<string> codes, CancellationToken cancellationToken = default);

    [InquirySelectAllByPredicate(OrderBy = "Id ASC")]
    [InquiryWhere("Code", Compare.In, Not = true)]
    public partial Task<IReadOnlyList<SmallInItem>> OutsideCodesAsync(IReadOnlyList<string> codes, CancellationToken cancellationToken = default);

    [InquiryDelete, InquiryWhere("Id", Compare.In)]
    public partial Task<int> DeleteAllAsync(IReadOnlyList<int> ids, CancellationToken cancellationToken = default);
}

/// <summary>
/// Shared live checks for #509. Lists up to the dialect threshold bind as padded scalar parameters, and
/// longer, empty and negated lists keep the collection transport. Both paths must return the same rows.
/// </summary>
internal static class SmallInListScenarios
{
    /// <summary>Seeds ids 1-2 in category 1, ids 3-4 in category 2 and id 5 with no category.</summary>
    internal static async Task SeedAsync(SmallInItemStore store)
    {
        await store.InsertAsync(new SmallInItem { Id = 1, CategoryId = 1, Code = "alpha" });
        await store.InsertAsync(new SmallInItem { Id = 2, CategoryId = 1, Code = "Beta" });
        await store.InsertAsync(new SmallInItem { Id = 3, CategoryId = 2, Code = "gamma" });
        await store.InsertAsync(new SmallInItem { Id = 4, CategoryId = 2, Code = "delta" });
        await store.InsertAsync(new SmallInItem { Id = 5, CategoryId = null, Code = null });
    }

    /// <summary>
    /// Every length up to the threshold binds one padded bucket and no transport parameter; the next length
    /// uses the transport. The rows never change.
    /// </summary>
    internal static async Task ShortListsBindScalarBucketsAsync(SmallInItemStore store, RecordingCommandInterceptor recorder, int scalarLimit, string transportMarker)
    {
        await SeedAsync(store);
        var texts = new HashSet<string>(StringComparer.Ordinal);
        for (var length = 1; length <= Math.Max(scalarLimit, 8) + 1; length++)
        {
            recorder.Clear();
            var rows = await store.InCategoriesAsync(WithFiller([1], length));

            Assert.Equal([1, 2], rows.Select(static row => row.Id));
            var command = Assert.Single(recorder.Commands);
            if (length <= scalarLimit)
            {
                Assert.DoesNotContain(transportMarker, command.CommandText, StringComparison.Ordinal);
                Assert.Equal((int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)length), command.Parameters.Count);
                texts.Add(command.CommandText);
            }
            else
            {
                Assert.Contains(transportMarker, command.CommandText, StringComparison.Ordinal);
                Assert.Single(command.Parameters);
            }
        }

        // One statement text per power-of-two bucket.
        var buckets = scalarLimit == 0 ? 0 : System.Numerics.BitOperations.Log2((uint)scalarLimit) + 1;
        Assert.Equal(buckets, texts.Count);

        var duplicates = await store.InCategoriesAsync([1, 1, 1]);
        Assert.Equal([1, 2], duplicates.Select(static row => row.Id));
    }

    /// <summary>
    /// An empty list keeps the transport, so an empty IN matches nothing and its negation matches every row.
    /// A negated short list and a negated long list exclude the same rows, including the NULL category.
    /// </summary>
    internal static async Task EmptyAndNegatedListsKeepTheirMeaningAsync(SmallInItemStore store, int scalarLimit)
    {
        await SeedAsync(store);

        Assert.Empty(await store.InCategoriesAsync([]));
        Assert.Equal([1, 2, 3, 4, 5], (await store.OutsideCategoriesAsync([])).Select(static row => row.Id));
        Assert.Equal([3, 4], (await store.OutsideCategoriesAsync([1])).Select(static row => row.Id));
        Assert.Equal([3, 4], (await store.OutsideCategoriesAsync(WithFiller([1], scalarLimit + 1))).Select(static row => row.Id));
    }

    /// <summary>The paged COUNT repeats the IN criterion; both statements must see the same list.</summary>
    internal static async Task PagedTotalsMatchOnBothPathsAsync(SmallInItemStore store, int scalarLimit)
    {
        await SeedAsync(store);

        foreach (var categories in new[] { new List<int> { 1, 2 }, WithFiller([1, 2], scalarLimit + 1) })
        {
            var page = await store.PageInCategoriesAsync(categories, 1, 2);
            Assert.Equal(4, page.TotalCount);
            Assert.Equal([2, 3], page.Items.Select(static row => row.Id));
        }
    }

    /// <summary>Two IN criteria choose their binding independently.</summary>
    internal static async Task TwoListsBindIndependentlyAsync(SmallInItemStore store, int scalarLimit)
    {
        await SeedAsync(store);

        Assert.Equal([2, 3], (await store.InCategoriesAndIdsAsync([1, 2], [2, 3, 5])).Select(static row => row.Id));
        Assert.Equal([2, 3], (await store.InCategoriesAndIdsAsync(WithFiller([1, 2], scalarLimit + 1), [2, 3, 5])).Select(static row => row.Id));
        Assert.Equal([2, 3], (await store.InCategoriesAndIdsAsync([1, 2], WithFiller([2, 3, 5], scalarLimit + 1))).Select(static row => row.Id));
    }

    /// <summary>
    /// A short string list keeps the collection transport, because each transport converts a string to its own
    /// SQL type, width and collation. Short and long lists therefore return the same rows, including values that
    /// differ only in case or trailing spaces.
    /// </summary>
    internal static async Task ShortStringListsKeepTheTransportAsync(SmallInItemStore store, RecordingCommandInterceptor recorder, int scalarLimit, string transportMarker)
    {
        await SeedAsync(store);

        foreach (var codes in new[] { new List<string> { "alpha" }, ["ALPHA", "beta"], ["gamma ", "Delta"], ["missing"] })
        {
            recorder.Clear();
            var shortIds = (await store.InCodesAsync(codes)).Select(static row => row.Id).ToList();
            var command = Assert.Single(recorder.Commands);
            Assert.Contains(transportMarker, command.CommandText, StringComparison.Ordinal);
            Assert.Single(command.Parameters);

            var longIds = (await store.InCodesAsync(WithFillerCodes(codes, scalarLimit + 1))).Select(static row => row.Id).ToList();
            Assert.Equal(longIds, shortIds);
        }

        Assert.Equal([1], (await store.InCodesAsync(["alpha"])).Select(static row => row.Id));
    }

    /// <summary>
    /// Sol review round 3 (#509): a string longer than the transport's text width must not change a negated IN
    /// result with list length. A 1-value and a 33-value list holding the same oversized value return the same rows.
    /// </summary>
    internal static async Task OversizedStringOutsideListMatchesOnBothLengthsAsync(SmallInItemStore store)
    {
        await SeedAsync(store);
        var oversized = new string('x', 4001);

        var oneValue = (await store.OutsideCodesAsync([oversized])).Select(static row => row.Id).ToList();
        var manyValues = (await store.OutsideCodesAsync(WithFillerCodes([oversized], 33))).Select(static row => row.Id).ToList();

        Assert.Equal(manyValues, oneValue);
    }

    /// <summary>A delete by a short list and by a long list each remove exactly the listed rows.</summary>
    internal static async Task DeletesRemoveListedRowsOnBothPathsAsync(SmallInItemStore store, int scalarLimit)
    {
        await SeedAsync(store);

        Assert.Equal(1, await store.DeleteAllAsync([1]));
        Assert.Equal(2, await store.DeleteAllAsync(WithFiller([2, 3], scalarLimit + 1)));
        Assert.Equal([4, 5], (await store.OutsideCategoriesAsync([])).Select(static row => row.Id));
    }

    // Pads a list with codes that match no row until it has the requested length.
    private static List<string> WithFillerCodes(List<string> values, int length)
    {
        var list = new List<string>(values);
        list.AddRange(Enumerable.Range(0, Math.Max(0, length - values.Count)).Select(static i => "filler-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        return list;
    }

    // Pads a list with ids that match no row until it has the requested length.
    private static List<int> WithFiller(List<int> values, int length)
    {
        var list = new List<int>(values);
        list.AddRange(Enumerable.Range(1000, Math.Max(0, length - values.Count)));
        return list;
    }
}
