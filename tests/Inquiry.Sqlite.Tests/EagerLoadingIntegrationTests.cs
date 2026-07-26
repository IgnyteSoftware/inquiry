using Inquiry.Interceptors;
using Inquiry.Northwind;
using Inquiry.Northwind.Models;
using Inquiry.Northwind.Stores;
using Inquiry.Sqlite.Tests.Fixtures;
using Inquiry.Testing;
using Inquiry.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace Inquiry.Sqlite.Tests;

/// <summary>
/// Eager loading is exercised against two relationships:
/// <list type="bullet">
///   <item><see cref="Region"/>↔<see cref="Territory"/> — non-nullable PK and FK on both sides.</item>
///   <item><see cref="Category"/>↔<see cref="Product"/> — nullable IDENTITY PK and nullable FK,
///         exercising the null-skip / null-short-circuit paths the generator emits.</item>
/// </list>
/// </summary>
public sealed class EagerLoadingIntegrationTests
{
    [Fact]
    public async Task SelectOneByKeyEagerLoadsChildCollection()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "Eager");
        var regionStore = harness.GetRequiredService<RegionStore>();
        var territoryStore = harness.GetRequiredService<TerritoryStore>();

        var region = new Region { RegionID = 1, RegionDescription = "Eastern" };
        await regionStore.InsertAsync(region);

        foreach (var t in new[]
        {
            new Territory { TerritoryID = "01581", TerritoryDescription = "Westboro", RegionID = 1 },
            new Territory { TerritoryID = "01730", TerritoryDescription = "Bedford",  RegionID = 1 },
        })
        {
            await territoryStore.InsertAsync(t);
        }

        var loaded = await regionStore.SelectByKeyWithTerritoriesAsync(1);

        Assert.NotNull(loaded);
        Assert.Equal("Eastern", loaded.RegionDescription);
        Assert.NotNull(loaded.Territories);
        Assert.Equal(2, loaded.Territories!.Count);
        Assert.Contains(loaded.Territories, t => t.TerritoryDescription == "Westboro");
        Assert.Contains(loaded.Territories, t => t.TerritoryDescription == "Bedford");
    }

    [Fact]
    public async Task SelectOneByKeyEagerReturnsNullForMissingEntity()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "Eager");
        var regionStore = harness.GetRequiredService<RegionStore>();

        var loaded = await regionStore.SelectByKeyWithTerritoriesAsync(99);
        Assert.Null(loaded);
    }

    [Fact]
    public async Task SelectOneByKeyEagerReturnsEmptyCollectionWhenNoChildren()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "Eager");
        var regionStore = harness.GetRequiredService<RegionStore>();

        await regionStore.InsertAsync(new Region { RegionID = 5, RegionDescription = "Empty" });

        var loaded = await regionStore.SelectByKeyWithTerritoriesAsync(5);

        Assert.NotNull(loaded);
        Assert.NotNull(loaded.Territories);
        Assert.Empty(loaded.Territories!);
    }

    [Fact]
    public async Task SelectAllEagerPopulatesChildrenForAllParents()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "Eager");
        var regionStore = harness.GetRequiredService<RegionStore>();
        var territoryStore = harness.GetRequiredService<TerritoryStore>();

        await regionStore.InsertAsync(new Region { RegionID = 1, RegionDescription = "Eastern" });
        await regionStore.InsertAsync(new Region { RegionID = 2, RegionDescription = "Western" });

        foreach (var t in new[]
        {
            new Territory { TerritoryID = "T1", TerritoryDescription = "E1", RegionID = 1 },
            new Territory { TerritoryID = "T2", TerritoryDescription = "E2", RegionID = 1 },
            new Territory { TerritoryID = "T3", TerritoryDescription = "W1", RegionID = 2 },
        })
        {
            await territoryStore.InsertAsync(t);
        }

        var all = await regionStore.SelectAllWithTerritoriesAsync().ToListAsync();

        Assert.Equal(2, all.Count);
        var eastern = all.Single(r => r.RegionDescription == "Eastern");
        var western = all.Single(r => r.RegionDescription == "Western");
        Assert.Equal(2, eastern.Territories?.Count);
        Assert.Equal(1, western.Territories?.Count);
    }

    [Fact]
    public async Task SelectOneByKeyEagerLoadsParentReference()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "Eager");
        var regionStore = harness.GetRequiredService<RegionStore>();
        var territoryStore = harness.GetRequiredService<TerritoryStore>();

        await regionStore.InsertAsync(new Region { RegionID = 1, RegionDescription = "Eastern" });
        await territoryStore.InsertAsync(new Territory { TerritoryID = "T1", TerritoryDescription = "Boston", RegionID = 1 });

        var loaded = await territoryStore.SelectByKeyWithRegionAsync("T1");

        Assert.NotNull(loaded);
        Assert.Equal("Boston", loaded.TerritoryDescription);
        Assert.NotNull(loaded.Region);
        Assert.Equal(1, loaded.Region!.RegionID);
        Assert.Equal("Eastern", loaded.Region.RegionDescription);
    }

    [Fact]
    public async Task SelectAllEagerPopulatesParentForAllChildren()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "Eager");
        var regionStore = harness.GetRequiredService<RegionStore>();
        var territoryStore = harness.GetRequiredService<TerritoryStore>();

        await regionStore.InsertAsync(new Region { RegionID = 1, RegionDescription = "Eastern" });
        await regionStore.InsertAsync(new Region { RegionID = 2, RegionDescription = "Western" });

        foreach (var t in new[]
        {
            new Territory { TerritoryID = "T1", TerritoryDescription = "E1", RegionID = 1 },
            new Territory { TerritoryID = "T2", TerritoryDescription = "E2", RegionID = 1 },
            new Territory { TerritoryID = "T3", TerritoryDescription = "W1", RegionID = 2 },
        })
        {
            await territoryStore.InsertAsync(t);
        }

        var all = await territoryStore.SelectAllWithRegionAsync().ToListAsync();

        Assert.Equal(3, all.Count);
        Assert.All(all, t => Assert.NotNull(t.Region));
        Assert.Equal("Eastern", all.Single(t => t.TerritoryDescription == "E1").Region!.RegionDescription);
        Assert.Equal("Eastern", all.Single(t => t.TerritoryDescription == "E2").Region!.RegionDescription);
        Assert.Equal("Western", all.Single(t => t.TerritoryDescription == "W1").Region!.RegionDescription);
    }

    [Fact]
    public async Task SelectByKeyEagerLeavesParentNullForOrphanForeignKey()
    {
        // FK off so the orphan insert is allowed; otherwise SQLite rejects it.
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "Eager", foreignKeys: false);
        var territoryStore = harness.GetRequiredService<TerritoryStore>();

        await territoryStore.InsertAsync(new Territory { TerritoryID = "T999", TerritoryDescription = "Orphan", RegionID = 99 });

        var loaded = await territoryStore.SelectByKeyWithRegionAsync("T999");

        Assert.NotNull(loaded);
        Assert.Null(loaded.Region);
    }

    [Fact]
    public async Task SelectAllEagerLeavesParentNullForOrphanForeignKey()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "Eager", foreignKeys: false);
        var regionStore = harness.GetRequiredService<RegionStore>();
        var territoryStore = harness.GetRequiredService<TerritoryStore>();

        await regionStore.InsertAsync(new Region { RegionID = 1, RegionDescription = "Existing" });

        await territoryStore.InsertAsync(new Territory { TerritoryID = "T1",   TerritoryDescription = "Matched", RegionID = 1 });
        await territoryStore.InsertAsync(new Territory { TerritoryID = "T999", TerritoryDescription = "Orphan",  RegionID = 99 });

        var all = await territoryStore.SelectAllWithRegionAsync().ToListAsync();

        Assert.Equal(2, all.Count);
        Assert.Equal("Existing", all.Single(t => t.TerritoryDescription == "Matched").Region?.RegionDescription);
        Assert.Null(all.Single(t => t.TerritoryDescription == "Orphan").Region);
    }

    // ---- Nullable-key cases: Category (int? PK) ↔ Product (int? FK) ----

    [Fact]
    public async Task SelectOneByKeyEagerLoadsChildCollectionWithNullableKeys()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "EagerNullable");
        var categoryStore = harness.GetRequiredService<CategoryStore>();
        var productStore = harness.GetRequiredService<ProductStore>();

        var beverages = await categoryStore.InsertReturningAsync(new Category { CategoryName = "Beverages" });
        Assert.NotNull(beverages);

        await productStore.InsertAsync(new Product { ProductName = "Chai",  CategoryID = beverages!.CategoryID });
        await productStore.InsertAsync(new Product { ProductName = "Chang", CategoryID = beverages.CategoryID });

        var loaded = await categoryStore.SelectByKeyWithProductsAsync(beverages.CategoryID);

        Assert.NotNull(loaded);
        Assert.NotNull(loaded!.Products);
        Assert.Equal(2, loaded.Products!.Count);
        Assert.Contains(loaded.Products, p => p.ProductName == "Chai");
        Assert.Contains(loaded.Products, p => p.ProductName == "Chang");
    }

    [Fact]
    public async Task SelectAllEagerSkipsChildrenWithNullForeignKey()
    {
        // FK off so the orphan product (CategoryID = null) is allowed.
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "EagerNullable", foreignKeys: false);
        var categoryStore = harness.GetRequiredService<CategoryStore>();
        var productStore = harness.GetRequiredService<ProductStore>();

        var beverages = await categoryStore.InsertReturningAsync(new Category { CategoryName = "Beverages" });
        Assert.NotNull(beverages);

        await productStore.InsertAsync(new Product { ProductName = "Chai",         CategoryID = beverages!.CategoryID });
        await productStore.InsertAsync(new Product { ProductName = "Uncategorized", CategoryID = null });

        var all = await categoryStore.SelectAllWithProductsAsync().ToListAsync();

        var loaded = Assert.Single(all);
        Assert.NotNull(loaded.Products);
        var product = Assert.Single(loaded.Products!);
        Assert.Equal("Chai", product.ProductName);
    }

    [Fact]
    public async Task SelectAllEagerPopulatesParentForChildWithNullableForeignKey()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "EagerNullable");
        var categoryStore = harness.GetRequiredService<CategoryStore>();
        var productStore = harness.GetRequiredService<ProductStore>();

        var beverages = await categoryStore.InsertReturningAsync(new Category { CategoryName = "Beverages" });
        Assert.NotNull(beverages);

        await productStore.InsertAsync(new Product { ProductName = "Chai", CategoryID = beverages!.CategoryID });

        var all = await productStore.SelectAllWithCategoryAsync().ToListAsync();

        var loaded = Assert.Single(all);
        Assert.NotNull(loaded.Category);
        Assert.Equal("Beverages", loaded.Category!.CategoryName);
    }

    [Fact]
    public async Task SelectAllEagerLeavesParentNullWhenForeignKeyIsNull()
    {
        // FK off so the null-FK product is allowed.
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "EagerNullable", foreignKeys: false);
        var categoryStore = harness.GetRequiredService<CategoryStore>();
        var productStore = harness.GetRequiredService<ProductStore>();

        var beverages = await categoryStore.InsertReturningAsync(new Category { CategoryName = "Beverages" });
        Assert.NotNull(beverages);

        await productStore.InsertAsync(new Product { ProductName = "Chai",         CategoryID = beverages!.CategoryID });
        await productStore.InsertAsync(new Product { ProductName = "Uncategorized", CategoryID = null });

        var all = await productStore.SelectAllWithCategoryAsync().ToListAsync();

        Assert.Equal(2, all.Count);
        Assert.Equal("Beverages", all.Single(p => p.ProductName == "Chai").Category?.CategoryName);
        Assert.Null(all.Single(p => p.ProductName == "Uncategorized").Category);
    }

    // ---- Round-trip assertions: exactly one command, and it went through the grid path.
    // See EagerGridCommandAssertions.AssertSingleGridCommand for why both signals are required. ----

    [Fact]
    public async Task SelectOneByKeyEagerUsesGridPath()
    {
        var (harness, probe, recorder) = await CreateGridHarnessAsync();
        await using var _ = harness;

        await EagerGridCommandAssertions.SelectOneByKeyEagerIssuesOneCommandAsync(
            harness.GetRequiredService<RegionStore>(),
            harness.GetRequiredService<TerritoryStore>(),
            probe, recorder);
    }

    [Fact]
    public async Task SelectAllEagerUsesGridPath()
    {
        var (harness, probe, recorder) = await CreateGridHarnessAsync();
        await using var _ = harness;

        await EagerGridCommandAssertions.SelectAllEagerIssuesOneCommandAsync(
            harness.GetRequiredService<RegionStore>(),
            harness.GetRequiredService<TerritoryStore>(),
            probe, recorder);
    }

    [Fact]
    public async Task SelectOneByKeyEagerWithReferenceUsesGridPath()
    {
        var (harness, probe, recorder) = await CreateGridHarnessAsync();
        await using var _ = harness;

        await EagerGridCommandAssertions.SelectOneByKeyEagerWithReferenceIssuesOneCommandAsync(
            harness.GetRequiredService<RegionStore>(),
            harness.GetRequiredService<TerritoryStore>(),
            probe, recorder);
    }

    [Fact]
    public async Task SelectAllEagerWithReferenceUsesGridPath()
    {
        var (harness, probe, recorder) = await CreateGridHarnessAsync();
        await using var _ = harness;

        await EagerGridCommandAssertions.SelectAllEagerWithReferenceIssuesOneCommandAsync(
            harness.GetRequiredService<RegionStore>(),
            harness.GetRequiredService<TerritoryStore>(),
            probe, recorder);
    }

    // ---- Streaming-parent behaviour (#70). SelectAllEager reads child sets first and streams parents out
    // of the grid's last result set, so there is no buffered parent list and no zero-parent early-out. ----

    [Fact]
    public async Task SelectAllEagerReturnsEmptyWhenThereAreNoParents()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "EagerEmpty");
        var regionStore = harness.GetRequiredService<RegionStore>();

        var all = await regionStore.SelectAllWithTerritoriesAsync().ToListAsync();

        Assert.Empty(all);
    }

    [Fact]
    public async Task SelectAllEagerAbandonedMidStreamStillStitchesAndLeavesHarnessUsable()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "EagerBreak");
        var regionStore = harness.GetRequiredService<RegionStore>();
        var territoryStore = harness.GetRequiredService<TerritoryStore>();

        await regionStore.InsertAsync(new Region { RegionID = 1, RegionDescription = "Eastern" });
        await regionStore.InsertAsync(new Region { RegionID = 2, RegionDescription = "Western" });
        await territoryStore.InsertAsync(new Territory { TerritoryID = "T1", TerritoryDescription = "Boston", RegionID = 1 });
        await territoryStore.InsertAsync(new Territory { TerritoryID = "T2", TerritoryDescription = "Denver", RegionID = 2 });

        // Take the first parent and walk away — the grid is disposed mid-result-set.
        Region? first = null;
        await foreach (var region in regionStore.SelectAllWithTerritoriesAsync())
        {
            first = region;
            break;
        }

        Assert.NotNull(first);
        Assert.Single(first!.Territories!);

        // Abandoning the stream must not wedge the connection for subsequent work.
        var all = await regionStore.SelectAllWithTerritoriesAsync().ToListAsync();
        Assert.Equal(2, all.Count);
        Assert.All(all, r => Assert.Single(r.Territories!));
    }

    private static async Task<(SqliteTestHarness Harness, BatchExecutionProbe Probe, RecordingCommandInterceptor Recorder)> CreateGridHarnessAsync()
    {
        var probe = new BatchExecutionProbe();
        var recorder = new RecordingCommandInterceptor();
        var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "EagerGrid",
            configureServices: s =>
            {
                s.AddSingleton<IInquiryCommandInterceptor>(recorder);
                probe.Decorate(s);
            });
        return (harness, probe, recorder);
    }
}
