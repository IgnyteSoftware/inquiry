using Inquiry.Northwind;
using Inquiry.Northwind.Models;
using Inquiry.Northwind.Stores;
using Inquiry.Sqlite.Tests.Fixtures;

namespace Inquiry.Sqlite.Tests;

/// <summary>
/// End-to-end coverage of <c>[InquirySelectAllByPredicate]</c> over the Northwind <c>Product</c>
/// entity: every supported operator (comparison, LIKE, BETWEEN, IN non-empty/empty, IS NULL) plus a
/// single OR-group, run against real SQLite.
/// </summary>
public sealed class PredicateSelectIntegrationTests
{
    private static async Task<SqliteTestHarness> SeedAsync()
    {
        var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "Predicate", foreignKeys: false);
        var products = harness.GetRequiredService<ProductStore>();

        await products.InsertAsync(new Product { ProductName = "Chai", UnitPrice = 18m, UnitsInStock = 39, CategoryID = 1, Discontinued = false });
        await products.InsertAsync(new Product { ProductName = "Chang", UnitPrice = 19m, UnitsInStock = 17, CategoryID = 1, Discontinued = false });
        await products.InsertAsync(new Product { ProductName = "Aniseed Syrup", UnitPrice = 10m, UnitsInStock = 13, CategoryID = 2, Discontinued = false });
        await products.InsertAsync(new Product { ProductName = "Chef Anton's Cajun", UnitPrice = 22m, UnitsInStock = 53, CategoryID = 2, Discontinued = true });
        await products.InsertAsync(new Product { ProductName = "Uncategorized", UnitPrice = 5m, UnitsInStock = 0, CategoryID = null, Discontinued = true });

        return harness;
    }

    [Fact]
    public async Task ComparisonAndLikeFilterRows()
    {
        await using var harness = await SeedAsync();
        var products = harness.GetRequiredService<ProductStore>();

        // UnitPrice >= 18 AND ProductName LIKE 'Ch%'
        var matched = await products.SearchAsync(18m, "Ch%");

        Assert.Equal(3, matched.Count);
        Assert.All(matched, p => Assert.StartsWith("Ch", p.ProductName));
        Assert.All(matched, p => Assert.True(p.UnitPrice >= 18m));
    }

    [Fact]
    public async Task BetweenFilterIsInclusive()
    {
        await using var harness = await SeedAsync();
        var products = harness.GetRequiredService<ProductStore>();

        // UnitsInStock BETWEEN 13 AND 39
        var matched = await products.InStockRangeAsync(13, 39);

        Assert.Equal(3, matched.Count);
        Assert.All(matched, p => Assert.InRange(p.UnitsInStock!.Value, (short)13, (short)39));
    }

    [Fact]
    public async Task InFilterMatchesAnyListedValue()
    {
        await using var harness = await SeedAsync();
        var products = harness.GetRequiredService<ProductStore>();

        var matched = await products.InCategoriesAsync(new[] { 2 });

        Assert.Equal(2, matched.Count);
        Assert.All(matched, p => Assert.Equal(2, p.CategoryID));
    }

    [Fact]
    public async Task InFilterWithMultipleValuesMatchesUnion()
    {
        await using var harness = await SeedAsync();
        var products = harness.GetRequiredService<ProductStore>();

        var matched = await products.InCategoriesAsync(new[] { 1, 2 });

        Assert.Equal(4, matched.Count);
    }

    [Fact]
    public async Task EmptyInFilterMatchesNoRows()
    {
        await using var harness = await SeedAsync();
        var products = harness.GetRequiredService<ProductStore>();

        var matched = await products.InCategoriesAsync(System.Array.Empty<int>());

        Assert.Empty(matched);
    }

    [Fact]
    public async Task IsNullFilterMatchesNullColumn()
    {
        await using var harness = await SeedAsync();
        var products = harness.GetRequiredService<ProductStore>();

        var matched = await products.WithoutCategoryAsync();

        var only = Assert.Single(matched);
        Assert.Null(only.CategoryID);
        Assert.Equal("Uncategorized", only.ProductName);
    }

    [Fact]
    public async Task OrGroupMatchesEitherCriterion()
    {
        await using var harness = await SeedAsync();
        var products = harness.GetRequiredService<ProductStore>();

        // Discontinued = true OR UnitsInStock < 15
        var matched = await products.DiscontinuedOrLowStockAsync(true, 15);

        // Discontinued: "Chef Anton's Cajun", "Uncategorized"; low stock (<15): "Aniseed Syrup" (13),
        // "Uncategorized" (0). Union = 3 distinct rows.
        Assert.Equal(3, matched.Count);
        Assert.All(matched, p => Assert.True(p.Discontinued || p.UnitsInStock < 15));
    }
}
