using Inquiry.Northwind.Models;
using Inquiry.Northwind.Stores;
using Inquiry.PostgreSql.Tests.Fixtures;

namespace Inquiry.PostgreSql.Tests;

/// <summary>
/// End-to-end coverage of W2 ORDER BY + pagination over the Northwind <c>Product</c> entity against
/// real PostgreSQL: ordered results, offset page boundaries, keyset forward paging round-tripping
/// <c>NextCursor</c>, the null first page, <c>HasMore</c> at the end, and a multi-column keyset tie-break.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
public sealed class PaginationIntegrationTests
{
    private readonly PostgreSqlContainerFixture _fixture;
    public PaginationIntegrationTests(PostgreSqlContainerFixture fixture) => _fixture = fixture;

    // Seeds five products with distinct names. ProductID is database-generated and assigned in insert
    // order (1..5), so name order differs from id order — useful for tie-break / ordering assertions.
    private static async Task SeedAsync(PostgreSqlTestHarness harness)
    {
        var categories = harness.GetRequiredService<CategoryStore>();
        var products = harness.GetRequiredService<ProductStore>();

        var c1 = (await categories.InsertReturningAsync(new Category { CategoryName = "C1" }))!.CategoryID;
        var c2 = (await categories.InsertReturningAsync(new Category { CategoryName = "C2" }))!.CategoryID;
        var c3 = (await categories.InsertReturningAsync(new Category { CategoryName = "C3" }))!.CategoryID;

        await products.InsertAsync(new Product { ProductName = "Echo",    UnitPrice = 1m, CategoryID = c1, Discontinued = false });
        await products.InsertAsync(new Product { ProductName = "Alpha",   UnitPrice = 2m, CategoryID = c1, Discontinued = false });
        await products.InsertAsync(new Product { ProductName = "Delta",   UnitPrice = 3m, CategoryID = c2, Discontinued = false });
        await products.InsertAsync(new Product { ProductName = "Bravo",   UnitPrice = 4m, CategoryID = c2, Discontinued = false });
        await products.InsertAsync(new Product { ProductName = "Charlie", UnitPrice = 5m, CategoryID = c3, Discontinued = false });
    }

    [SkippableFact]
    public async Task OrderByReturnsRowsInSortedOrder()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await PostgreSqlTestHarness.CreateAsync(_fixture.AdminConnectionString, "pageorder");
        await SeedAsync(harness);
        var products = harness.GetRequiredService<ProductStore>();

        var ordered = await products.SelectAllOrderedByNameAsync();

        Assert.Equal(
            new[] { "Alpha", "Bravo", "Charlie", "Delta", "Echo" },
            ordered.Select(p => p.ProductName).ToArray());
    }

    [SkippableFact]
    public async Task OffsetPagingWalksPageBoundaries()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await PostgreSqlTestHarness.CreateAsync(_fixture.AdminConnectionString, "pageoffset");
        await SeedAsync(harness);
        var products = harness.GetRequiredService<ProductStore>();

        // Ordered by ProductID ASC (insert order 1..5).
        var page1 = await products.PageByIdAsync(offset: 0, limit: 2);
        var page2 = await products.PageByIdAsync(offset: 2, limit: 2);

        Assert.Equal(new[] { "Echo", "Alpha" }, page1.Select(p => p.ProductName).ToArray());
        Assert.Equal(new[] { "Delta", "Bravo" }, page2.Select(p => p.ProductName).ToArray());
    }

    [SkippableFact]
    public async Task OffsetPagingLastPageIsPartial()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await PostgreSqlTestHarness.CreateAsync(_fixture.AdminConnectionString, "pagelast");
        await SeedAsync(harness);
        var products = harness.GetRequiredService<ProductStore>();

        var lastPage = await products.PageByIdAsync(offset: 4, limit: 2);

        var only = Assert.Single(lastPage);
        Assert.Equal("Charlie", only.ProductName);
    }

    [SkippableFact]
    public async Task OffsetPagingPastEndIsEmpty()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await PostgreSqlTestHarness.CreateAsync(_fixture.AdminConnectionString, "pagepast");
        await SeedAsync(harness);
        var products = harness.GetRequiredService<ProductStore>();

        var empty = await products.PageByIdAsync(offset: 10, limit: 2);

        Assert.Empty(empty);
    }

    [SkippableFact]
    public async Task KeysetForwardPagingRoundTripsCursor()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await PostgreSqlTestHarness.CreateAsync(_fixture.AdminConnectionString, "keyset");
        await SeedAsync(harness);
        var products = harness.GetRequiredService<ProductStore>();

        var page1 = await products.KeysetByIdAsync(afterProductID: null, pageSize: 2);
        Assert.Equal(new[] { "Echo", "Alpha" }, page1.Items.Select(p => p.ProductName).ToArray());
        Assert.True(page1.HasMore);
        Assert.Equal(2, page1.NextCursor);

        var page2 = await products.KeysetByIdAsync(afterProductID: page1.NextCursor, pageSize: 2);
        Assert.Equal(new[] { "Delta", "Bravo" }, page2.Items.Select(p => p.ProductName).ToArray());
        Assert.True(page2.HasMore);
        Assert.Equal(4, page2.NextCursor);

        var page3 = await products.KeysetByIdAsync(afterProductID: page2.NextCursor, pageSize: 2);
        var only = Assert.Single(page3.Items);
        Assert.Equal("Charlie", only.ProductName);
        Assert.False(page3.HasMore);
        Assert.Equal(5, page3.NextCursor);
    }

    [SkippableFact]
    public async Task KeysetHasMoreIsFalseWhenPageSizeMatchesRemaining()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await PostgreSqlTestHarness.CreateAsync(_fixture.AdminConnectionString, "keysetexact");
        await SeedAsync(harness);
        var products = harness.GetRequiredService<ProductStore>();

        var page = await products.KeysetByIdAsync(afterProductID: null, pageSize: 5);

        Assert.Equal(5, page.Items.Count);
        Assert.False(page.HasMore);
    }

    [SkippableFact]
    public async Task MultiColumnKeysetBreaksTiesAndRoundTrips()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await PostgreSqlTestHarness.CreateAsync(_fixture.AdminConnectionString, "keysetmulti");
        await SeedAsync(harness);
        var products = harness.GetRequiredService<ProductStore>();

        // Add a duplicate name so (ProductName, ProductID) ordering must tie-break on ProductID.
        await products.InsertAsync(new Product { ProductName = "Alpha", UnitPrice = 9m, CategoryID = null, Discontinued = false });

        var page1 = await products.KeysetByNameThenIdAsync(after: null, pageSize: 2);
        // Two "Alpha" rows order by ascending ProductID: original (id 2) then the new one (id 6).
        Assert.Equal(new[] { "Alpha", "Alpha" }, page1.Items.Select(p => p.ProductName).ToArray());
        Assert.Equal(new[] { 2, 6 }, page1.Items.Select(p => p.ProductID!.Value).ToArray());
        Assert.True(page1.HasMore);

        var page2 = await products.KeysetByNameThenIdAsync(after: page1.NextCursor, pageSize: 2);
        Assert.Equal(new[] { "Bravo", "Charlie" }, page2.Items.Select(p => p.ProductName).ToArray());
    }

    [SkippableFact]
    public async Task KeysetEmptyTableYieldsNullCursorAndNoMore()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await PostgreSqlTestHarness.CreateAsync(_fixture.AdminConnectionString, "keysetempty");
        var products = harness.GetRequiredService<ProductStore>();

        var page = await products.KeysetByIdAsync(afterProductID: null, pageSize: 3);

        Assert.Empty(page.Items);
        Assert.False(page.HasMore);
        Assert.Null(page.NextCursor);
    }
}
