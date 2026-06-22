using Inquiry.Northwind.Models;
using Inquiry.Northwind.Stores;
using Inquiry.Oracle.Tests.Fixtures;

namespace Inquiry.Oracle.Tests;

/// <summary>
/// <c>[InquirySelectAllByPredicate]</c> over the Northwind <c>Product</c> entity against real Oracle.
/// Comparison / LIKE / BETWEEN / OR / IS NULL / IN predicates all work. Every predicate parameter — the
/// scalar binds and the <c>IN</c>-expansion sentinel — takes Oracle's <c>:name</c> sigil, and
/// <c>OracleInquiryConnectionFactory.FinalizeCommand</c> reconciles the per-element expansion parameters
/// with the runtime binder under <c>BindByName</c>.
/// </summary>
[Collection(OracleCollection.Name)]
public sealed class PredicateSelectIntegrationTests
{
    private readonly OracleContainerFixture _fixture;
    public PredicateSelectIntegrationTests(OracleContainerFixture fixture) => _fixture = fixture;

    // Oracle does not support result-set RETURNING, so generated keys are read back via SelectAll
    // rather than InsertReturning. Returns the two category ids so IN-filter assertions do not depend on
    // a particular identity seed value.
    private static async Task<(int C1, int C2)> SeedAsync(OracleTestHarness harness)
    {
        var categories = harness.GetRequiredService<CategoryStore>();
        var products = harness.GetRequiredService<ProductStore>();

        await categories.InsertAsync(new Category { CategoryName = "Beverages" });
        await categories.InsertAsync(new Category { CategoryName = "Condiments" });
        var cats = await categories.SelectAllAsync().ToListAsync();
        var c1 = cats.Single(c => c.CategoryName == "Beverages").CategoryID!.Value;
        var c2 = cats.Single(c => c.CategoryName == "Condiments").CategoryID!.Value;

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
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "predlike");
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
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "predbetween");
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
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "predin");
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
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "predinmulti");
        var (c1, c2) = await SeedAsync(harness);
        var products = harness.GetRequiredService<ProductStore>();

        var matched = await products.InCategoriesAsync(new[] { c1, c2 });

        Assert.Equal(4, matched.Count);
    }

    [SkippableFact]
    public async Task EmptyInFilterMatchesNoRows()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "predinempty");
        await SeedAsync(harness);
        var products = harness.GetRequiredService<ProductStore>();

        var matched = await products.InCategoriesAsync(System.Array.Empty<int>());

        Assert.Empty(matched);
    }

    [SkippableFact]
    public async Task LargeInListStaysBelowOracleInListLimit()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "predinlarge");
        var (c1, _) = await SeedAsync(harness);
        var products = harness.GetRequiredService<ProductStore>();

        // 600 elements: the next power-of-two bucket (1024) exceeds Oracle's 1000-entry IN-list limit
        // (ORA-01795). The expansion must leave the list at its exact length rather than padding into a
        // runtime error. c1 plus 599 non-matching filler ids → still returns exactly c1's two products.
        var ids = new List<int> { c1 };
        for (var i = 0; i < 599; i++)
        {
            ids.Add(1_000_000 + i);
        }

        var matched = await products.InCategoriesAsync(ids);

        Assert.Equal(2, matched.Count);
        Assert.All(matched, p => Assert.Equal(c1, p.CategoryID));
    }

    [SkippableFact]
    public async Task IsNullFilterMatchesNullColumn()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "prednull");
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
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "predor");
        await SeedAsync(harness);
        var products = harness.GetRequiredService<ProductStore>();

        // Discontinued = true OR UnitsInStock < 15
        var matched = await products.DiscontinuedOrLowStockAsync(true, 15);

        Assert.Equal(3, matched.Count);
        Assert.All(matched, p => Assert.True(p.Discontinued || p.UnitsInStock < 15));
    }
}
