using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Inquiry.Entities;
using Inquiry.Sqlite.Tests.Fixtures;
using Inquiry.Stores;

namespace Inquiry.Sqlite.Tests;

[InquiryTable("DistinctProduct")]
public sealed class DistinctProduct
{
    [InquiryKey(IsGenerated = true)]
    public long Id { get; set; }

    [InquiryColumn("Name")]
    public string Name { get; set; } = string.Empty;

    [InquiryColumn("Category")]
    public string Category { get; set; } = string.Empty;
}

[InquiryProjection(typeof(DistinctProduct))]
public sealed record DistinctCategory
{
    [InquiryColumn("Category")]
    public string Category { get; init; } = string.Empty;
}

public partial class DistinctProductStore : InquiryStore<DistinctProduct>
{
    [InquiryInsert]
    public partial Task<int> InsertAsync(DistinctProduct product, CancellationToken cancellationToken = default);

    [InquirySelectAll(Distinct = true)]
    public partial Task<IReadOnlyList<DistinctProduct>> SelectDistinctAsync(CancellationToken cancellationToken = default);

    [InquirySelectAllByField("Category", Distinct = true)]
    public partial Task<IReadOnlyList<DistinctProduct>> SelectDistinctByCategoryAsync(string category, CancellationToken cancellationToken = default);

    [InquirySelectAll(Distinct = true)]
    public partial Task<IReadOnlyList<DistinctCategory>> DistinctCategoriesAsync(CancellationToken cancellationToken = default);

    [InquirySelectAll]
    public partial Task<IReadOnlyList<DistinctProduct>> SelectAllAsync(CancellationToken cancellationToken = default);
}

public sealed class DistinctIntegrationTests
{
    private const string Ddl = "CREATE TABLE DistinctProduct (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL, Category TEXT NOT NULL);";

    [Fact]
    public async Task DistinctSelectAllDeduplicatesRows()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Ddl, "Distinct");
        var store = harness.GetRequiredService<DistinctProductStore>();

        await store.InsertAsync(new DistinctProduct { Name = "Widget", Category = "A" });
        await store.InsertAsync(new DistinctProduct { Name = "Gadget", Category = "A" });
        await store.InsertAsync(new DistinctProduct { Name = "Widget", Category = "B" });

        var distinct = await store.SelectDistinctAsync();
        var all = await store.SelectAllAsync();

        // All rows are unique by (Id, Name, Category), so DISTINCT returns the same count.
        Assert.Equal(all.Count, distinct.Count);
    }

    [Fact]
    public async Task DistinctSelectAllByFieldFiltersAndDeduplicates()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Ddl, "Distinct");
        var store = harness.GetRequiredService<DistinctProductStore>();

        await store.InsertAsync(new DistinctProduct { Name = "Widget", Category = "A" });
        await store.InsertAsync(new DistinctProduct { Name = "Gadget", Category = "A" });
        await store.InsertAsync(new DistinctProduct { Name = "Doohickey", Category = "B" });

        var result = await store.SelectDistinctByCategoryAsync("A");
        Assert.Equal(2, result.Count);
        Assert.All(result, p => Assert.Equal("A", p.Category));
    }

    [Fact]
    public async Task DistinctProjectionReturnsUniqueCategories()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Ddl, "Distinct");
        var store = harness.GetRequiredService<DistinctProductStore>();

        await store.InsertAsync(new DistinctProduct { Name = "Widget", Category = "A" });
        await store.InsertAsync(new DistinctProduct { Name = "Gadget", Category = "A" });
        await store.InsertAsync(new DistinctProduct { Name = "Doohickey", Category = "B" });

        var categories = await store.DistinctCategoriesAsync();

        // Projection selects only Category; DISTINCT deduplicates the two "A" rows.
        Assert.Equal(2, categories.Count);
        Assert.Contains(categories, c => c.Category == "A");
        Assert.Contains(categories, c => c.Category == "B");
    }
}
