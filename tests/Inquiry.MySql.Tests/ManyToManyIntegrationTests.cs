using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Inquiry.FeatureCatalog;
using Inquiry.MySql.Tests.Fixtures;

namespace Inquiry.MySql.Tests;

/// <summary>
/// End-to-end many-to-many eager loading against real MySQL: a single-parent eager load joins the
/// related rows through the junction, and the all-eager load assembles every parent's collection in
/// memory from two queries.
/// </summary>
[Collection(MySqlCollection.Name)]
public sealed class ManyToManyIntegrationTests
{
    private readonly MySqlContainerFixture _fixture;
    public ManyToManyIntegrationTests(MySqlContainerFixture fixture) => _fixture = fixture;

    private async Task<(MySqlTestHarness Harness, M2MOrderStore Orders, long Order1, long Order2)> SeedAsync()
    {
        var harness = await MySqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, FeatureSchema.ManyToManyMySqlDdl, "m2m");
        var orders = harness.GetRequiredService<M2MOrderStore>();
        var products = harness.GetRequiredService<M2MProductStore>();
        var links = harness.GetRequiredService<M2MOrderProductStore>();

        var order1 = (await orders.InsertAsync(new M2MOrder { Name = "First" }))!.Id;
        var order2 = (await orders.InsertAsync(new M2MOrder { Name = "Second" }))!.Id;
        var apple = (await products.InsertAsync(new M2MProduct { Title = "Apple" }))!.Id;
        var banana = (await products.InsertAsync(new M2MProduct { Title = "Banana" }))!.Id;
        var cherry = (await products.InsertAsync(new M2MProduct { Title = "Cherry" }))!.Id;

        // Order1 → Apple, Banana; Order2 → Banana, Cherry.
        await links.LinkAsync(new M2MOrderProduct { OrderId = order1, ProductId = apple });
        await links.LinkAsync(new M2MOrderProduct { OrderId = order1, ProductId = banana });
        await links.LinkAsync(new M2MOrderProduct { OrderId = order2, ProductId = banana });
        await links.LinkAsync(new M2MOrderProduct { OrderId = order2, ProductId = cherry });

        return (harness, orders, order1, order2);
    }

    [SkippableFact]
    public async Task SingleEagerLoadsRelatedRowsThroughJunction()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        var (harness, orders, order1, _) = await SeedAsync();
        await using var _ = harness;

        var loaded = await orders.GetWithProductsAsync(order1);
        Assert.NotNull(loaded);
        Assert.Equal("First", loaded!.Name);
        Assert.Equal(new[] { "Apple", "Banana" }, loaded.Products.Select(p => p.Title).OrderBy(t => t).ToArray());
    }

    [SkippableFact]
    public async Task AllEagerAssemblesEveryParentsCollection()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        var (harness, orders, _, _) = await SeedAsync();
        await using var _ = harness;

        var all = new List<M2MOrder>();
        await foreach (var order in orders.AllWithProductsAsync())
        {
            all.Add(order);
        }

        Assert.Equal(2, all.Count);
        var first = all.Single(o => o.Name == "First");
        var second = all.Single(o => o.Name == "Second");
        Assert.Equal(new[] { "Apple", "Banana" }, first.Products.Select(p => p.Title).OrderBy(t => t).ToArray());
        Assert.Equal(new[] { "Banana", "Cherry" }, second.Products.Select(p => p.Title).OrderBy(t => t).ToArray());
    }

    [SkippableFact]
    public async Task AllEagerDoesNotMaterializeUnrelatedOrFilteredRows()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        var (harness, orders, order1, _) = await SeedAsync();
        await using var _ = harness;
        var scenario = await M2MExcludedRowsScenario.SeedAsync(
            orders,
            harness.GetRequiredService<M2MProductStore>(),
            harness.GetRequiredService<M2MOrderProductStore>(),
            order1);

        M2MMaterializationProbe.Reset(scenario.DefaultExcludedTitles, scenario.DefaultExcludedProductIds);
        var single = await orders.GetWithProductsAsync(order1);
        Assert.NotNull(single);
        Assert.DoesNotContain(single!.Products,
            p => p.Title is "Deleted junction" or "Inactive junction");

        M2MMaterializationProbe.Reset(scenario.DefaultExcludedTitles, scenario.DefaultExcludedProductIds);

        var all = await orders.AllWithProductsAsync().ToListAsync();

        Assert.True(M2MMaterializationProbe.ChildReads > 0);
        Assert.True(M2MMaterializationProbe.JunctionReads > 0);
        Assert.Equal(0, M2MMaterializationProbe.ExcludedChildReads);
        Assert.Equal(0, M2MMaterializationProbe.ExcludedJunctionReads);
    }

    [SkippableFact]
    public async Task IncludeDeletedEagerUsesMatchingParentScopeAndKeepsRelationFilters()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        var (harness, orders, order1, _) = await SeedAsync();
        await using var _ = harness;
        var scenario = await M2MExcludedRowsScenario.SeedAsync(
            orders,
            harness.GetRequiredService<M2MProductStore>(),
            harness.GetRequiredService<M2MOrderProductStore>(),
            order1);
        M2MMaterializationProbe.Reset(
            scenario.IncludeDeletedExcludedTitles,
            scenario.IncludeDeletedExcludedProductIds);

        var all = await orders.AllIncludingDeletedWithProductsAsync().ToListAsync();

        var deletedParent = all.Single(o => o.Id == scenario.DeletedParentId);
        Assert.Contains(deletedParent.Products, p => p.Title == scenario.DeletedParentIncludedTitle);
        Assert.True(M2MMaterializationProbe.ChildReads > 0);
        Assert.True(M2MMaterializationProbe.JunctionReads > 0);
        Assert.Equal(0, M2MMaterializationProbe.ExcludedChildReads);
        Assert.Equal(0, M2MMaterializationProbe.ExcludedJunctionReads);
    }

    [SkippableFact]
    public async Task EagerCollectionIsEmptyWhenNoAssociations()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, FeatureSchema.ManyToManyMySqlDdl, "m2m");
        var orders = harness.GetRequiredService<M2MOrderStore>();
        var lonely = (await orders.InsertAsync(new M2MOrder { Name = "Lonely" }))!.Id;

        var loaded = await orders.GetWithProductsAsync(lonely);
        Assert.NotNull(loaded);
        Assert.Empty(loaded!.Products);
    }
}
