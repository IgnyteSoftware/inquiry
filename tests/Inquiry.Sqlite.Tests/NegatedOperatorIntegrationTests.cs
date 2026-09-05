using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Inquiry.Entities;
using Inquiry.Sqlite.Tests.Fixtures;
using Inquiry.Stores;

namespace Inquiry.Sqlite.Tests;

[InquiryTable("NegatedProduct")]
public sealed class NegatedProduct
{
    [InquiryKey(IsGenerated = true)]
    public long Id { get; set; }

    [InquiryColumn("Name")]
    public string Name { get; set; } = string.Empty;

    [InquiryColumn("Qty")]
    public int Qty { get; set; }
}

public partial class NegatedProductStore : InquiryStore<NegatedProduct>
{
    [InquiryInsert]
    public partial Task<NegatedProduct?> InsertAsync(NegatedProduct product, CancellationToken cancellationToken = default);

    [InquirySelectAllByPredicate]
    [InquiryWhere("Name", Compare.NotLike)]
    public partial Task<IReadOnlyList<NegatedProduct>> NameNotLikeAsync(string pattern, CancellationToken cancellationToken = default);

    [InquirySelectAllByPredicate]
    [InquiryWhere("Qty", Compare.NotBetween)]
    public partial Task<IReadOnlyList<NegatedProduct>> QtyNotBetweenAsync(int low, int high, CancellationToken cancellationToken = default);

    [InquirySelectAllByPredicate]
    [InquiryWhere("Qty", Compare.NotIn)]
    public partial Task<IReadOnlyList<NegatedProduct>> QtyNotInAsync(IReadOnlyList<int> qtys, CancellationToken cancellationToken = default);
}

/// <summary>
/// End-to-end negated predicate operators against real SQLite: <c>NotLike</c> excludes pattern matches
/// and <c>NotBetween</c> excludes the inclusive range.
/// </summary>
public sealed class NegatedOperatorIntegrationTests
{
    private const string Ddl =
        "CREATE TABLE NegatedProduct (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL, Qty INTEGER NOT NULL);";

    private static async Task<(SqliteTestHarness Harness, NegatedProductStore Store)> SeedAsync()
    {
        var harness = await SqliteTestHarness.CreateAsync(Ddl, "Negated");
        var store = harness.GetRequiredService<NegatedProductStore>();
        await store.InsertAsync(new NegatedProduct { Name = "Widget", Qty = 5 });
        await store.InsertAsync(new NegatedProduct { Name = "Gadget", Qty = 15 });
        await store.InsertAsync(new NegatedProduct { Name = "Gizmo Test", Qty = 25 });
        return (harness, store);
    }

    [Fact]
    public async Task NotLikeExcludesPatternMatches()
    {
        var (harness, store) = await SeedAsync();
        await using var _ = harness;

        // Everything whose name does NOT contain "Test".
        var result = await store.NameNotLikeAsync("%Test%");
        Assert.Equal(new[] { "Gadget", "Widget" }, result.Select(p => p.Name).OrderBy(n => n).ToArray());
    }

    [Fact]
    public async Task NotBetweenExcludesInclusiveRange()
    {
        var (harness, store) = await SeedAsync();
        await using var _ = harness;

        // Qty NOT BETWEEN 10 AND 20 → keeps 5 and 25, excludes 15.
        var result = await store.QtyNotBetweenAsync(10, 20);
        Assert.Equal(new[] { 5, 25 }, result.Select(p => p.Qty).OrderBy(q => q).ToArray());
    }

    [Fact]
    public async Task NotInExcludesListedValues()
    {
        var (harness, store) = await SeedAsync();
        await using var _ = harness;

        // Qty NOT IN (15, 25) → keeps only 5.
        var result = await store.QtyNotInAsync(new[] { 15, 25 });
        var only = Assert.Single(result);
        Assert.Equal(5, only.Qty);
    }

    [Fact]
    public async Task EmptyNotInMatchesEveryRow()
    {
        var (harness, store) = await SeedAsync();
        await using var _ = harness;

        // An empty NOT IN excludes nothing — every row matches (the opposite of an empty IN).
        var result = await store.QtyNotInAsync(System.Array.Empty<int>());
        Assert.Equal(3, result.Count);
    }
}
