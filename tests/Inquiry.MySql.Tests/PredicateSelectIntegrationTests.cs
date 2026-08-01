using System;
using System.Collections.Generic;
using Inquiry.Northwind.Models;
using Inquiry.Northwind.Stores;
using Inquiry.MySql.Tests.Fixtures;

namespace Inquiry.MySql.Tests;

/// <summary>
/// End-to-end coverage of <c>[InquirySelectAllByPredicate]</c> over the Northwind <c>Product</c>
/// entity against real MySQL: every supported operator (comparison, LIKE, BETWEEN, IN non-empty/empty,
/// IS NULL) plus a single OR-group.
/// </summary>
[Collection(MySqlCollection.Name)]
public sealed class PredicateSelectIntegrationTests
{
    private readonly MySqlContainerFixture _fixture;
    public PredicateSelectIntegrationTests(MySqlContainerFixture fixture) => _fixture = fixture;

    // Seeds five products across two categories (plus one uncategorized) and returns the generated
    // category ids so IN-filter assertions do not depend on a particular identity seed value.
    private static async Task<(int C1, int C2)> SeedAsync(MySqlTestHarness harness)
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
        await using var harness = await MySqlTestHarness.CreateAsync(_fixture.AdminConnectionString, "predlike");
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
        await using var harness = await MySqlTestHarness.CreateAsync(_fixture.AdminConnectionString, "predbetween");
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
        await using var harness = await MySqlTestHarness.CreateAsync(_fixture.AdminConnectionString, "predin");
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
        await using var harness = await MySqlTestHarness.CreateAsync(_fixture.AdminConnectionString, "predinmulti");
        var (c1, c2) = await SeedAsync(harness);
        var products = harness.GetRequiredService<ProductStore>();

        var matched = await products.InCategoriesAsync(new[] { c1, c2 });

        Assert.Equal(4, matched.Count);
    }

    [SkippableFact]
    public async Task EmptyInFilterMatchesNoRows()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateAsync(_fixture.AdminConnectionString, "predinempty");
        await SeedAsync(harness);
        var products = harness.GetRequiredService<ProductStore>();

        var matched = await products.InCategoriesAsync(System.Array.Empty<int>());

        Assert.Empty(matched);
    }

    [SkippableFact]
    public async Task IsNullFilterMatchesNullColumn()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateAsync(_fixture.AdminConnectionString, "prednull");
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
        await using var harness = await MySqlTestHarness.CreateAsync(_fixture.AdminConnectionString, "predor");
        await SeedAsync(harness);
        var products = harness.GetRequiredService<ProductStore>();

        // Discontinued = true OR UnitsInStock < 15
        var matched = await products.DiscontinuedOrLowStockAsync(true, 15);

        // Discontinued: "Chef Anton's Cajun", "Uncategorized"; low stock (<15): "Aniseed Syrup" (13),
        // "Uncategorized" (0). Union = 3 distinct rows.
        Assert.Equal(3, matched.Count);
        Assert.All(matched, p => Assert.True(p.Discontinued || p.UnitsInStock < 15));
    }

    // #106: live bucket-boundary coverage. #67 pads each IN list to the next power of two by repeating an
    // element; these cardinalities (1,2,3,5,9 → buckets 1,2,4,8,16) prove the padded SQL returns the same
    // rows on real MySQL — the "results unchanged by padding" guarantee, previously only live on SQL Server.
    [SkippableFact]
    public async Task InListBucketBoundariesReturnCorrectRows()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateAsync(_fixture.AdminConnectionString, "inbucket");
        var (c1, c2) = await SeedAsync(harness);
        var products = harness.GetRequiredService<ProductStore>();

        foreach (var k in new[] { 1, 2, 3, 5, 9 })
        {
            var ids = new List<int> { c1 };
            for (var i = 1; i < k; i++)
            {
                ids.Add(c2 + 1000 + i); // filler that matches no category
            }

            var matched = await products.InCategoriesAsync(ids);
            Assert.Equal(2, matched.Count);
            Assert.All(matched, p => Assert.Equal(c1, p.CategoryID));
        }

        // A pure-duplicate list (bucket 4, padded by repeating the value) never widens the match set.
        var dup = await products.InCategoriesAsync(new List<int> { c1, c1, c1 });
        Assert.Equal(2, dup.Count);
        Assert.All(dup, p => Assert.Equal(c1, p.CategoryID));
    }

    // #106: live NOT IN bucketing on a real engine. Padding repeats a value (col<>v AND col<>v is a no-op)
    // and never uses NULL (a NULL in NOT IN makes the predicate UNKNOWN); the excluded set must be exact.
    [SkippableFact]
    public async Task NotInListBucketingExcludesCorrectSetAndEmptyMatchesAll()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateAsync(_fixture.AdminConnectionString, "notinbucket");
        var (c1, c2) = await SeedAsync(harness);
        var products = harness.GetRequiredService<ProductStore>();

        // NOT IN (c2) excludes category 2's two products; the null-category row is also excluded
        // (NULL NOT IN (…) is UNKNOWN), leaving category 1's two products.
        var notC2 = await products.NotInCategoriesAsync(new List<int> { c2 });
        Assert.Equal(2, notC2.Count);
        Assert.All(notC2, p => Assert.Equal(c1, p.CategoryID));

        // A padded NOT IN (bucket 4, repeating c2) excludes the same set.
        var notC2Padded = await products.NotInCategoriesAsync(new List<int> { c2, c2, c2 });
        Assert.Equal(2, notC2Padded.Count);

        // An empty NOT IN excludes nothing → matches every row (all five products).
        var all = await products.NotInCategoriesAsync(Array.Empty<int>());
        Assert.Equal(5, all.Count);
    }
}
