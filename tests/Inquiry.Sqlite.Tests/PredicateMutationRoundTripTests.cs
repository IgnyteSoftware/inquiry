using Inquiry.Sqlite.Tests.Fixtures;

namespace Inquiry.Sqlite.Tests;

/// <summary>
/// Runtime round-trips for the set-based predicate mutations: <c>[InquiryUpdate]</c> and
/// <c>[InquiryDelete]</c> must affect exactly the rows matching their <c>[InquiryWhere]</c>
/// criteria and return the affected-row count.
/// </summary>
public sealed class PredicateMutationRoundTripTests
{
    private const string Schema = """
        CREATE TABLE TPredicateMutationItem (
            Id INTEGER PRIMARY KEY,
            Category TEXT NOT NULL,
            Price NUMERIC NOT NULL
        );
        """;

    private static async Task<(SqliteTestHarness Harness, PredicateMutationItemStore Store)> CreateSeededAsync()
    {
        var harness = await SqliteTestHarness.CreateAsync(Schema, "PredicateMutationItem");
        var store = harness.GetRequiredService<PredicateMutationItemStore>();
        await store.InsertAsync(new PredicateMutationItem { Category = "book", Price = 10m });
        await store.InsertAsync(new PredicateMutationItem { Category = "book", Price = 20m });
        await store.InsertAsync(new PredicateMutationItem { Category = "toy", Price = 30m });
        return (harness, store);
    }

    [Fact]
    public async Task UpdateWhereAffectsOnlyMatchingRowsAndReturnsCount()
    {
        var (harness, store) = await CreateSeededAsync();
        await using var _ = harness;

        var affected = await store.RepriceCategoryAsync(5.5m, "book");

        Assert.Equal(2, affected);
        var all = await store.SelectAllAsync();
        Assert.Equal(3, all.Count);
        Assert.All(all.Where(i => i.Category == "book"), i => Assert.Equal(5.5m, i.Price));
        Assert.Equal(30m, Assert.Single(all, i => i.Category == "toy").Price);
    }

    [Fact]
    public async Task DeleteWhereRemovesOnlyMatchingRowsAndReturnsCount()
    {
        var (harness, store) = await CreateSeededAsync();
        await using var _ = harness;

        var affected = await store.DeleteCategoryAsync("book");

        Assert.Equal(2, affected);
        var remaining = await store.SelectAllAsync();
        var survivor = Assert.Single(remaining);
        Assert.Equal("toy", survivor.Category);
        Assert.Equal(30m, survivor.Price);
    }

    [Fact]
    public async Task UpdateWhereWithNoMatchesReturnsZero()
    {
        var (harness, store) = await CreateSeededAsync();
        await using var _ = harness;

        var affected = await store.RepriceCategoryAsync(1m, "absent");

        Assert.Equal(0, affected);
        var all = await store.SelectAllAsync();
        Assert.DoesNotContain(all, i => i.Price == 1m);
    }
}
