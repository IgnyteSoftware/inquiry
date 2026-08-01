using Inquiry.Northwind;
using Inquiry.Northwind.Models;
using Inquiry.Northwind.Stores;
using Inquiry.Sqlite.Tests.Fixtures;

namespace Inquiry.Sqlite.Tests;

/// <summary>
/// End-to-end coverage of ORDER BY + pagination over the Northwind <c>Product</c> entity against
/// real SQLite: ordered results, offset page boundaries (including the last partial page and an empty
/// page past the end), keyset forward paging round-tripping <c>NextCursor</c>, the null first page,
/// <c>HasMore</c> at the end, and a multi-column keyset tie-break.
/// </summary>
public sealed class PaginationIntegrationTests
{
    // Seeds five products with distinct names. ProductID is database-generated and assigned in insert
    // order (1..5), so name order differs from id order — useful for tie-break / ordering assertions.
    private static async Task<SqliteTestHarness> SeedAsync()
    {
        var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "Paging", foreignKeys: false);
        var products = harness.GetRequiredService<ProductStore>();

        await products.InsertAsync(new Product { ProductName = "Echo", UnitPrice = 1m, CategoryID = 1, Discontinued = false });
        await products.InsertAsync(new Product { ProductName = "Alpha", UnitPrice = 2m, CategoryID = 1, Discontinued = false });
        await products.InsertAsync(new Product { ProductName = "Delta", UnitPrice = 3m, CategoryID = 2, Discontinued = false });
        await products.InsertAsync(new Product { ProductName = "Bravo", UnitPrice = 4m, CategoryID = 2, Discontinued = false });
        await products.InsertAsync(new Product { ProductName = "Charlie", UnitPrice = 5m, CategoryID = 3, Discontinued = false });

        return harness;
    }

    [Fact]
    public async Task OrderByReturnsRowsInSortedOrder()
    {
        await using var harness = await SeedAsync();
        var products = harness.GetRequiredService<ProductStore>();

        var ordered = await products.SelectAllOrderedByNameAsync();

        Assert.Equal(
            new[] { "Alpha", "Bravo", "Charlie", "Delta", "Echo" },
            ordered.Select(p => p.ProductName).ToArray());
    }

    [Fact]
    public async Task OffsetPagingWalksPageBoundaries()
    {
        await using var harness = await SeedAsync();
        var products = harness.GetRequiredService<ProductStore>();

        // Ordered by ProductID ASC (insert order 1..5).
        var page1 = await products.PageByIdAsync(offset: 0, limit: 2);
        var page2 = await products.PageByIdAsync(offset: 2, limit: 2);

        Assert.Equal(new[] { "Echo", "Alpha" }, page1.Select(p => p.ProductName).ToArray());
        Assert.Equal(new[] { "Delta", "Bravo" }, page2.Select(p => p.ProductName).ToArray());
    }

    [Fact]
    public async Task OffsetPagingLastPageIsPartial()
    {
        await using var harness = await SeedAsync();
        var products = harness.GetRequiredService<ProductStore>();

        var lastPage = await products.PageByIdAsync(offset: 4, limit: 2);

        var only = Assert.Single(lastPage);
        Assert.Equal("Charlie", only.ProductName);
    }

    [Fact]
    public async Task OffsetPagingPastEndIsEmpty()
    {
        await using var harness = await SeedAsync();
        var products = harness.GetRequiredService<ProductStore>();

        var empty = await products.PageByIdAsync(offset: 10, limit: 2);

        Assert.Empty(empty);
    }

    [Fact]
    public async Task KeysetForwardPagingRoundTripsCursor()
    {
        await using var harness = await SeedAsync();
        var products = harness.GetRequiredService<ProductStore>();

        // Null first page selects from the start; HasMore true with rows remaining.
        var page1 = await products.KeysetByIdAsync(afterProductID: null, pageSize: 2);
        Assert.Equal(new[] { "Echo", "Alpha" }, page1.Items.Select(p => p.ProductName).ToArray());
        Assert.True(page1.HasMore);
        Assert.Equal(2, page1.NextCursor);

        // Round-trip the cursor into the next page.
        var page2 = await products.KeysetByIdAsync(afterProductID: page1.NextCursor, pageSize: 2);
        Assert.Equal(new[] { "Delta", "Bravo" }, page2.Items.Select(p => p.ProductName).ToArray());
        Assert.True(page2.HasMore);
        Assert.Equal(4, page2.NextCursor);

        // Final page: one row, HasMore false.
        var page3 = await products.KeysetByIdAsync(afterProductID: page2.NextCursor, pageSize: 2);
        var only = Assert.Single(page3.Items);
        Assert.Equal("Charlie", only.ProductName);
        Assert.False(page3.HasMore);
        Assert.Equal(5, page3.NextCursor);
    }

    [Fact]
    public async Task KeysetHasMoreIsFalseWhenPageSizeMatchesRemaining()
    {
        await using var harness = await SeedAsync();
        var products = harness.GetRequiredService<ProductStore>();

        // Exactly five rows, pageSize five -> all returned, HasMore false (the +1 over-fetch finds nothing).
        var page = await products.KeysetByIdAsync(afterProductID: null, pageSize: 5);

        Assert.Equal(5, page.Items.Count);
        Assert.False(page.HasMore);
    }

    [Fact]
    public async Task MultiColumnKeysetBreaksTiesAndRoundTrips()
    {
        await using var harness = await SeedAsync();
        var products = harness.GetRequiredService<ProductStore>();

        // Add a duplicate name so (ProductName, ProductID) ordering must tie-break on ProductID.
        await products.InsertAsync(new Product { ProductName = "Alpha", UnitPrice = 9m, CategoryID = 1, Discontinued = false });

        var page1 = await products.KeysetByNameThenIdAsync(after: null, pageSize: 2);
        // Two "Alpha" rows order by ascending ProductID: original (id 2) then the new one (id 6).
        Assert.Equal(new[] { "Alpha", "Alpha" }, page1.Items.Select(p => p.ProductName).ToArray());
        Assert.Equal(new[] { 2, 6 }, page1.Items.Select(p => p.ProductID!.Value).ToArray());
        Assert.True(page1.HasMore);

        var page2 = await products.KeysetByNameThenIdAsync(after: page1.NextCursor, pageSize: 2);
        Assert.Equal(new[] { "Bravo", "Charlie" }, page2.Items.Select(p => p.ProductName).ToArray());
    }

    [Fact]
    public async Task KeysetEmptyTableYieldsNullCursorAndNoMore()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "PagingEmpty", foreignKeys: false);
        var products = harness.GetRequiredService<ProductStore>();

        var page = await products.KeysetByIdAsync(afterProductID: null, pageSize: 3);

        Assert.Empty(page.Items);
        Assert.False(page.HasMore);
        Assert.Null(page.NextCursor);
    }

    // ---- Pagination argument validation (audit P2 #12) ----------------------------------
    //
    // Pre-fix the generated methods bound offset/limit/pageSize straight into the SQL with no
    // validation. Negative offsets fall through to the provider (provider-specific error or
    // silent wrong results); a non-positive limit/pageSize wastes a round trip (or, in the
    // keyset case, errors arithmetically since the SQL uses `pageSize + 1`); pageSize ==
    // int.MaxValue overflows that `+ 1`. The generator now emits explicit guards.

    [Fact]
    public async Task OffsetPagingRejectsNegativeOffset()
    {
        await using var harness = await SeedAsync();
        var products = harness.GetRequiredService<ProductStore>();

        var ex = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => products.PageByIdAsync(offset: -1, limit: 2));
        Assert.Equal("offset", ex.ParamName);
    }

    [Fact]
    public async Task OffsetPagingRejectsZeroLimit()
    {
        await using var harness = await SeedAsync();
        var products = harness.GetRequiredService<ProductStore>();

        var ex = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => products.PageByIdAsync(offset: 0, limit: 0));
        Assert.Equal("limit", ex.ParamName);
    }

    [Fact]
    public async Task OffsetPagingRejectsNegativeLimit()
    {
        await using var harness = await SeedAsync();
        var products = harness.GetRequiredService<ProductStore>();

        var ex = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => products.PageByIdAsync(offset: 0, limit: -5));
        Assert.Equal("limit", ex.ParamName);
    }

    [Fact]
    public async Task KeysetPagingRejectsZeroPageSize()
    {
        await using var harness = await SeedAsync();
        var products = harness.GetRequiredService<ProductStore>();

        var ex = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => products.KeysetByIdAsync(afterProductID: null, pageSize: 0));
        Assert.Equal("pageSize", ex.ParamName);
    }

    [Fact]
    public async Task KeysetPagingRejectsNegativePageSize()
    {
        await using var harness = await SeedAsync();
        var products = harness.GetRequiredService<ProductStore>();

        var ex = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => products.KeysetByIdAsync(afterProductID: null, pageSize: -1));
        Assert.Equal("pageSize", ex.ParamName);
    }

    [Fact]
    public async Task KeysetPagingRejectsIntMaxPageSizeToAvoidOverflow()
    {
        await using var harness = await SeedAsync();
        var products = harness.GetRequiredService<ProductStore>();

        // The keyset SQL over-fetches `pageSize + 1` to detect a next page. int.MaxValue would
        // overflow that arithmetic; the generated guard rejects it up front.
        var ex = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => products.KeysetByIdAsync(afterProductID: null, pageSize: int.MaxValue));
        Assert.Equal("pageSize", ex.ParamName);
    }
}
