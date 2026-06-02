using Inquiry.Northwind.Models;
using Inquiry.Northwind.Stores;
using Inquiry.PostgreSql.Tests.Fixtures;

namespace Inquiry.PostgreSql.Tests;

/// <summary>
/// End-to-end coverage of <c>[InquirySelectAllByPredicate]</c> over the Northwind <c>Product</c>
/// entity against real PostgreSQL: every supported operator (comparison, LIKE, BETWEEN, IN non-empty/empty,
/// IS NULL) plus a single OR-group.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
public sealed class PredicateSelectIntegrationTests
{
    private readonly PostgreSqlContainerFixture _fixture;
    public PredicateSelectIntegrationTests(PostgreSqlContainerFixture fixture) => _fixture = fixture;

    // Seeds five products across two categories (plus one uncategorized) and returns the generated
    // category ids so IN-filter assertions do not depend on a particular identity seed value.
    private static async Task<(int C1, int C2)> SeedAsync(PostgreSqlTestHarness harness)
    {
        var categories = harness.GetRequiredService<CategoryStore>();
        var products = harness.GetRequiredService<ProductStore>();

        var c1 = (await categories.InsertReturningAsync(new Category { CategoryName = "Beverages" }))!.CategoryID!.Value;
        var c2 = (await categories.InsertReturningAsync(new Category { CategoryName = "Condiments" }))!.CategoryID!.Value;

        await products.InsertAsync(new Product { ProductName = "Chai",               UnitPrice = 18m, UnitsInStock = 39, CategoryID = c1,   Discontinued = false });
        await products.InsertAsync(new Product { ProductName = "Chang",              UnitPrice = 19m, UnitsInStock = 17, CategoryID = c1,   Discontinued = false });
        await products.InsertAsync(new Product { ProductName = "Aniseed Syrup",      UnitPrice = 10m, UnitsInStock = 13, CategoryID = c2,   Discontinued = false });
        await products.InsertAsync(new Product { ProductName = "Chef Anton's Cajun", UnitPrice = 22m, UnitsInStock = 53, CategoryID = c2,   Discontinued = true });
        await products.InsertAsync(new Product { ProductName = "Uncategorized",      UnitPrice = 5m,  UnitsInStock = 0,  CategoryID = null, Discontinued = true });

        return (c1, c2);
    }

    [SkippableFact]
    public async Task ComparisonAndLikeFilterRows()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await PostgreSqlTestHarness.CreateAsync(_fixture.AdminConnectionString, "predlike");
        await SeedAsync(harness);
        var products = harness.GetRequiredService<ProductStore>();

        // UnitPrice >= 18 AND ProductName LIKE 'Ch%'
        var matched = await products.SearchAsync(18m, "Ch%");

        Assert.Equal(3, matched.Count);
        Assert.All(matched, p => Assert.StartsWith("Ch", p.ProductName));
        Assert.All(matched, p => Assert.True(p.UnitPrice >= 18m));
    }

    [SkippableFact]
    public async Task BetweenFilterIsInclusive()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await PostgreSqlTestHarness.CreateAsync(_fixture.AdminConnectionString, "predbetween");
        await SeedAsync(harness);
        var products = harness.GetRequiredService<ProductStore>();

        // UnitsInStock BETWEEN 13 AND 39
        var matched = await products.InStockRangeAsync(13, 39);

        Assert.Equal(3, matched.Count);
        Assert.All(matched, p => Assert.InRange(p.UnitsInStock!.Value, (short)13, (short)39));
    }

    [SkippableFact]
    public async Task InFilterMatchesAnyListedValue()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await PostgreSqlTestHarness.CreateAsync(_fixture.AdminConnectionString, "predin");
        var (_, c2) = await SeedAsync(harness);
        var products = harness.GetRequiredService<ProductStore>();

        var matched = await products.InCategoriesAsync(new[] { c2 });

        Assert.Equal(2, matched.Count);
        Assert.All(matched, p => Assert.Equal(c2, p.CategoryID));
    }

    [SkippableFact]
    public async Task InFilterWithMultipleValuesMatchesUnion()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await PostgreSqlTestHarness.CreateAsync(_fixture.AdminConnectionString, "predinmulti");
        var (c1, c2) = await SeedAsync(harness);
        var products = harness.GetRequiredService<ProductStore>();

        var matched = await products.InCategoriesAsync(new[] { c1, c2 });

        Assert.Equal(4, matched.Count);
    }

    [SkippableFact]
    public async Task EmptyInFilterMatchesNoRows()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await PostgreSqlTestHarness.CreateAsync(_fixture.AdminConnectionString, "predinempty");
        await SeedAsync(harness);
        var products = harness.GetRequiredService<ProductStore>();

        var matched = await products.InCategoriesAsync(System.Array.Empty<int>());

        Assert.Empty(matched);
    }

    [SkippableFact]
    public async Task IsNullFilterMatchesNullColumn()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await PostgreSqlTestHarness.CreateAsync(_fixture.AdminConnectionString, "prednull");
        await SeedAsync(harness);
        var products = harness.GetRequiredService<ProductStore>();

        var matched = await products.WithoutCategoryAsync();

        var only = Assert.Single(matched);
        Assert.Null(only.CategoryID);
        Assert.Equal("Uncategorized", only.ProductName);
    }

    [SkippableFact]
    public async Task OrGroupMatchesEitherCriterion()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await PostgreSqlTestHarness.CreateAsync(_fixture.AdminConnectionString, "predor");
        await SeedAsync(harness);
        var products = harness.GetRequiredService<ProductStore>();

        // Discontinued = true OR UnitsInStock < 15
        var matched = await products.DiscontinuedOrLowStockAsync(true, 15);

        // Discontinued: "Chef Anton's Cajun", "Uncategorized"; low stock (<15): "Aniseed Syrup" (13),
        // "Uncategorized" (0). Union = 3 distinct rows.
        Assert.Equal(3, matched.Count);
        Assert.All(matched, p => Assert.True(p.Discontinued || p.UnitsInStock < 15));
    }
}
