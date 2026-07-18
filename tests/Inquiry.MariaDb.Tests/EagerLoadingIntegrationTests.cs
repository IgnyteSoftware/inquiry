using Inquiry.Interceptors;
using Inquiry.MariaDb.Tests.Fixtures;
using Inquiry.Northwind;
using Inquiry.Northwind.Models;
using Inquiry.Northwind.Stores;
using Inquiry.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Inquiry.MariaDb.Tests;

/// <summary>
/// Eager loading is exercised against two relationships:
/// <list type="bullet">
///   <item><see cref="Region"/>↔<see cref="Territory"/> — non-nullable PK and FK on both sides.</item>
///   <item><see cref="Category"/>↔<see cref="Product"/> — nullable IDENTITY PK and nullable FK,
///         exercising the null-skip / null-short-circuit paths the generator emits.</item>
/// </list>
/// </summary>
[Collection(MariaDbCollection.Name)]
public sealed class EagerLoadingIntegrationTests
{
    private readonly MariaDbContainerFixture _fixture;

    public EagerLoadingIntegrationTests(MariaDbContainerFixture fixture) => _fixture = fixture;

    /// <summary>Region + Territories DDL without FK constraints for orphan-FK tests.</summary>
    private const string OrphanTerritoryDdl = """
        CREATE TABLE IF NOT EXISTS `Region` (
            `RegionID`           INT NOT NULL PRIMARY KEY,
            `RegionDescription`  VARCHAR(255) NOT NULL
        );

        CREATE TABLE IF NOT EXISTS `Territories` (
            `TerritoryID`           VARCHAR(40) NOT NULL PRIMARY KEY,
            `TerritoryDescription`  VARCHAR(255) NOT NULL,
            `RegionID`              INT NOT NULL
        );
        """;

    [SkippableFact]
    public async Task SelectOneByKeyEagerLoadsChildCollection()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);

        await using var harness = await MariaDbTestHarness.CreateAsync(_fixture.AdminConnectionString, "eager");
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

    [SkippableFact]
    public async Task SelectOneByKeyEagerReturnsNullForMissingEntity()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);

        await using var harness = await MariaDbTestHarness.CreateAsync(_fixture.AdminConnectionString, "eager");
        var regionStore = harness.GetRequiredService<RegionStore>();

        var loaded = await regionStore.SelectByKeyWithTerritoriesAsync(99);
        Assert.Null(loaded);
    }

    [SkippableFact]
    public async Task SelectOneByKeyEagerReturnsEmptyCollectionWhenNoChildren()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);

        await using var harness = await MariaDbTestHarness.CreateAsync(_fixture.AdminConnectionString, "eager");
        var regionStore = harness.GetRequiredService<RegionStore>();

        await regionStore.InsertAsync(new Region { RegionID = 5, RegionDescription = "Empty" });

        var loaded = await regionStore.SelectByKeyWithTerritoriesAsync(5);

        Assert.NotNull(loaded);
        Assert.NotNull(loaded.Territories);
        Assert.Empty(loaded.Territories!);
    }

    [SkippableFact]
    public async Task SelectAllEagerPopulatesChildrenForAllParents()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);

        await using var harness = await MariaDbTestHarness.CreateAsync(_fixture.AdminConnectionString, "eager");
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

    [SkippableFact]
    public async Task SelectOneByKeyEagerLoadsParentReference()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);

        await using var harness = await MariaDbTestHarness.CreateAsync(_fixture.AdminConnectionString, "eager");
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

    [SkippableFact]
    public async Task SelectAllEagerPopulatesParentForAllChildren()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);

        await using var harness = await MariaDbTestHarness.CreateAsync(_fixture.AdminConnectionString, "eager");
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

    [SkippableFact]
    public async Task SelectByKeyEagerLeavesParentNullForOrphanForeignKey()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);

        await using var harness = await MariaDbTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, OrphanTerritoryDdl, "eager");
        var territoryStore = harness.GetRequiredService<TerritoryStore>();

        await territoryStore.InsertAsync(new Territory { TerritoryID = "T999", TerritoryDescription = "Orphan", RegionID = 99 });

        var loaded = await territoryStore.SelectByKeyWithRegionAsync("T999");

        Assert.NotNull(loaded);
        Assert.Null(loaded.Region);
    }

    [SkippableFact]
    public async Task SelectAllEagerLeavesParentNullForOrphanForeignKey()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);

        await using var harness = await MariaDbTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, OrphanTerritoryDdl, "eager");
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

    [SkippableFact]
    public async Task SelectOneByKeyEagerLoadsChildCollectionWithNullableKeys()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);

        await using var harness = await MariaDbTestHarness.CreateAsync(_fixture.AdminConnectionString, "eager");
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

    [SkippableFact]
    public async Task SelectAllEagerSkipsChildrenWithNullForeignKey()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);

        await using var harness = await MariaDbTestHarness.CreateAsync(_fixture.AdminConnectionString, "eager");
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

    [SkippableFact]
    public async Task SelectAllEagerPopulatesParentForChildWithNullableForeignKey()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);

        await using var harness = await MariaDbTestHarness.CreateAsync(_fixture.AdminConnectionString, "eager");
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

    [SkippableFact]
    public async Task SelectAllEagerLeavesParentNullWhenForeignKeyIsNull()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);

        await using var harness = await MariaDbTestHarness.CreateAsync(_fixture.AdminConnectionString, "eager");
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

    // ---- Command-count assertions: the grid path (QueryMultipleAsync) bypasses the interceptor,
    // so zero intercepted commands + correct data proves one grid round trip was used. ----

    [SkippableFact]
    public async Task SelectOneByKeyEagerUsesGridPath()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);

        var recorder = new RecordingCommandInterceptor();
        await using var harness = await MariaDbTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString, NorthwindSchema.MySqlDdl, "eager",
            configureServices: s => s.AddSingleton<IInquiryCommandInterceptor>(recorder));
        var regionStore = harness.GetRequiredService<RegionStore>();
        var territoryStore = harness.GetRequiredService<TerritoryStore>();

        await regionStore.InsertAsync(new Region { RegionID = 1, RegionDescription = "Eastern" });
        await territoryStore.InsertAsync(new Territory { TerritoryID = "T1", TerritoryDescription = "Boston", RegionID = 1 });
        recorder.Clear();

        var loaded = await regionStore.SelectByKeyWithTerritoriesAsync(1);

        Assert.NotNull(loaded);
        Assert.Single(loaded.Territories!);
        Assert.Empty(recorder.Commands);
    }

    [SkippableFact]
    public async Task SelectAllEagerUsesGridPath()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);

        var recorder = new RecordingCommandInterceptor();
        await using var harness = await MariaDbTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString, NorthwindSchema.MySqlDdl, "eager",
            configureServices: s => s.AddSingleton<IInquiryCommandInterceptor>(recorder));
        var regionStore = harness.GetRequiredService<RegionStore>();
        var territoryStore = harness.GetRequiredService<TerritoryStore>();

        await regionStore.InsertAsync(new Region { RegionID = 1, RegionDescription = "Eastern" });
        await territoryStore.InsertAsync(new Territory { TerritoryID = "T1", TerritoryDescription = "Boston", RegionID = 1 });
        recorder.Clear();

        var all = await regionStore.SelectAllWithTerritoriesAsync().ToListAsync();

        Assert.Single(all);
        Assert.Single(all[0].Territories!);
        Assert.Empty(recorder.Commands);
    }

    [SkippableFact]
    public async Task SelectOneByKeyEagerWithReferenceUsesGridPath()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);

        var recorder = new RecordingCommandInterceptor();
        await using var harness = await MariaDbTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString, NorthwindSchema.MySqlDdl, "eager",
            configureServices: s => s.AddSingleton<IInquiryCommandInterceptor>(recorder));
        var regionStore = harness.GetRequiredService<RegionStore>();
        var territoryStore = harness.GetRequiredService<TerritoryStore>();

        await regionStore.InsertAsync(new Region { RegionID = 1, RegionDescription = "Eastern" });
        await territoryStore.InsertAsync(new Territory { TerritoryID = "T1", TerritoryDescription = "Boston", RegionID = 1 });
        recorder.Clear();

        var loaded = await territoryStore.SelectByKeyWithRegionAsync("T1");

        Assert.NotNull(loaded);
        Assert.NotNull(loaded.Region);
        Assert.Equal("Eastern", loaded.Region!.RegionDescription);
        Assert.Empty(recorder.Commands);
    }

    [SkippableFact]
    public async Task SelectAllEagerWithReferenceUsesGridPath()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);

        var recorder = new RecordingCommandInterceptor();
        await using var harness = await MariaDbTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString, NorthwindSchema.MySqlDdl, "eager",
            configureServices: s => s.AddSingleton<IInquiryCommandInterceptor>(recorder));
        var regionStore = harness.GetRequiredService<RegionStore>();
        var territoryStore = harness.GetRequiredService<TerritoryStore>();

        await regionStore.InsertAsync(new Region { RegionID = 1, RegionDescription = "Eastern" });
        await territoryStore.InsertAsync(new Territory { TerritoryID = "T1", TerritoryDescription = "Boston", RegionID = 1 });
        recorder.Clear();

        var all = await territoryStore.SelectAllWithRegionAsync().ToListAsync();

        Assert.Single(all);
        Assert.NotNull(all[0].Region);
        Assert.Empty(recorder.Commands);
    }
}
