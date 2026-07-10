using System;
using System.Collections.Generic;
using Inquiry.MariaDb.Tests.Fixtures;
using Inquiry.Northwind.Models;
using Inquiry.Northwind.Stores;

namespace Inquiry.MariaDb.Tests;

/// <summary>
/// Live coverage of IN-list bucketing and its NOT IN counterpart against real MariaDB: the expansion pads
/// each list to the next power of two by repeating an element, and these round-trips prove the padded SQL
/// returns the correct rows on a real engine across bucket boundaries.
/// </summary>
[Collection(MariaDbCollection.Name)]
public sealed class InListBucketingIntegrationTests
{
    private readonly MariaDbContainerFixture _fixture;
    public InListBucketingIntegrationTests(MariaDbContainerFixture fixture) => _fixture = fixture;

    private const string ProductsDdl = """
        CREATE TABLE `Products` (`ProductID` INT AUTO_INCREMENT PRIMARY KEY, `ProductName` VARCHAR(255) NOT NULL, `UnitPrice` DECIMAL(18,2), `UnitsInStock` SMALLINT, `CategoryID` INT NULL, `Discontinued` TINYINT(1) NOT NULL DEFAULT 0)
        """;

    private async Task<MariaDbTestHarness> SeedAsync()
    {
        var harness = await MariaDbTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, ProductsDdl, "inbucket");
        var products = harness.GetRequiredService<ProductStore>();

        // Two products in category 1, two in category 2, one uncategorized (null).
        await products.InsertAsync(new Product { ProductName = "Chai", UnitPrice = 18m, UnitsInStock = 39, CategoryID = 1, Discontinued = false });
        await products.InsertAsync(new Product { ProductName = "Chang", UnitPrice = 19m, UnitsInStock = 17, CategoryID = 1, Discontinued = false });
        await products.InsertAsync(new Product { ProductName = "Aniseed Syrup", UnitPrice = 10m, UnitsInStock = 13, CategoryID = 2, Discontinued = false });
        await products.InsertAsync(new Product { ProductName = "Chef Anton's Cajun", UnitPrice = 22m, UnitsInStock = 53, CategoryID = 2, Discontinued = true });
        await products.InsertAsync(new Product { ProductName = "Uncategorized", UnitPrice = 5m, UnitsInStock = 0, CategoryID = null, Discontinued = true });

        return harness;
    }

    [SkippableFact]
    public async Task InListBucketBoundaries_ReturnCorrectRowsAcrossCardinalities()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SeedAsync();
        var products = harness.GetRequiredService<ProductStore>();

        // Cardinalities 1,2,3,5,9 -> buckets 1,2,4,8,16. Each list holds category 1 plus non-matching
        // filler ids, so the matched set must be exactly category 1's two products for every cardinality.
        foreach (var k in new[] { 1, 2, 3, 5, 9 })
        {
            var ids = new List<int> { 1 };
            for (var i = 1; i < k; i++)
            {
                ids.Add(1000 + i);
            }

            var matched = await products.InCategoriesAsync(ids);
            Assert.Equal(2, matched.Count);
            Assert.All(matched, p => Assert.Equal(1, p.CategoryID));
        }

        // A pure-duplicate list (bucket 4, padded by repeating the value) never widens the match set.
        var dup = await products.InCategoriesAsync(new List<int> { 1, 1, 1 });
        Assert.Equal(2, dup.Count);
        Assert.All(dup, p => Assert.Equal(1, p.CategoryID));
    }

    [SkippableFact]
    public async Task NotInListBucketing_ExcludesCorrectSetAndEmptyMatchesAll()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SeedAsync();
        var products = harness.GetRequiredService<ProductStore>();

        // NOT IN (2) excludes category 2's two products; the null-category row is also excluded
        // (NULL NOT IN (...) is UNKNOWN), leaving category 1's two products.
        var notC2 = await products.NotInCategoriesAsync(new List<int> { 2 });
        Assert.Equal(2, notC2.Count);
        Assert.All(notC2, p => Assert.Equal(1, p.CategoryID));

        // A padded NOT IN (bucket 4, repeating 2) excludes the same set.
        var notC2Padded = await products.NotInCategoriesAsync(new List<int> { 2, 2, 2 });
        Assert.Equal(2, notC2Padded.Count);
        Assert.All(notC2Padded, p => Assert.Equal(1, p.CategoryID));

        // An empty NOT IN excludes nothing -> matches every row (all five products).
        var all = await products.NotInCategoriesAsync(Array.Empty<int>());
        Assert.Equal(5, all.Count);
    }
}
