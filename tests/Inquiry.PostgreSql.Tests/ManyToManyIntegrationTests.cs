using System.Collections.Generic;
using System.Linq;
using Inquiry.FeatureCatalog;
using Inquiry.PostgreSql.Tests.Fixtures;

namespace Inquiry.PostgreSql.Tests;

/// <summary>
/// End-to-end many-to-many eager loading against real PostgreSQL: a single-parent eager load joins the
/// related rows through the junction, and the all-eager load assembles every parent's collection in
/// memory from two queries.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
public sealed class ManyToManyIntegrationTests
{
    private readonly PostgreSqlContainerFixture _fixture;
    public ManyToManyIntegrationTests(PostgreSqlContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task SingleEagerLoadsRelatedRowsThroughJunction()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await PostgreSqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, FeatureSchema.ManyToManyPostgreSqlDdl, "m2m");
        var orders = harness.GetRequiredService<M2MOrderStore>();
        var products = harness.GetRequiredService<M2MProductStore>();
        var links = harness.GetRequiredService<M2MOrderProductStore>();

        var order1 = (await orders.InsertAsync(new M2MOrder { Name = "First" }))!.Id;
        var order2 = (await orders.InsertAsync(new M2MOrder { Name = "Second" }))!.Id;
        var apple = (await products.InsertAsync(new M2MProduct { Title = "Apple" }))!.Id;
        var banana = (await products.InsertAsync(new M2MProduct { Title = "Banana" }))!.Id;
        var cherry = (await products.InsertAsync(new M2MProduct { Title = "Cherry" }))!.Id;

        await links.LinkAsync(new M2MOrderProduct { OrderId = order1, ProductId = apple });
        await links.LinkAsync(new M2MOrderProduct { OrderId = order1, ProductId = banana });
        await links.LinkAsync(new M2MOrderProduct { OrderId = order2, ProductId = banana });
        await links.LinkAsync(new M2MOrderProduct { OrderId = order2, ProductId = cherry });

        var loaded = await orders.GetWithProductsAsync(order1);
        Assert.NotNull(loaded);
        Assert.Equal("First", loaded!.Name);
        Assert.Equal(new[] { "Apple", "Banana" }, loaded.Products.Select(p => p.Title).OrderBy(t => t).ToArray());
    }

    [SkippableFact]
    public async Task AllEagerAssemblesEveryParentsCollection()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await PostgreSqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, FeatureSchema.ManyToManyPostgreSqlDdl, "m2m");
        var orders = harness.GetRequiredService<M2MOrderStore>();
        var products = harness.GetRequiredService<M2MProductStore>();
        var links = harness.GetRequiredService<M2MOrderProductStore>();

        var order1 = (await orders.InsertAsync(new M2MOrder { Name = "First" }))!.Id;
        var order2 = (await orders.InsertAsync(new M2MOrder { Name = "Second" }))!.Id;
        var apple = (await products.InsertAsync(new M2MProduct { Title = "Apple" }))!.Id;
        var banana = (await products.InsertAsync(new M2MProduct { Title = "Banana" }))!.Id;
        var cherry = (await products.InsertAsync(new M2MProduct { Title = "Cherry" }))!.Id;

        await links.LinkAsync(new M2MOrderProduct { OrderId = order1, ProductId = apple });
        await links.LinkAsync(new M2MOrderProduct { OrderId = order1, ProductId = banana });
        await links.LinkAsync(new M2MOrderProduct { OrderId = order2, ProductId = banana });
        await links.LinkAsync(new M2MOrderProduct { OrderId = order2, ProductId = cherry });

        var all = await orders.AllWithProductsAsync().ToListAsync();

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
        await using var harness = await PostgreSqlTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString, FeatureSchema.ManyToManyPostgreSqlDdl, "m2m");
        var orders = harness.GetRequiredService<M2MOrderStore>();
        var products = harness.GetRequiredService<M2MProductStore>();
        var links = harness.GetRequiredService<M2MOrderProductStore>();
        var orderId = (await orders.InsertAsync(new M2MOrder { Name = "Participating" }))!.Id;
        var scenario = await M2MExcludedRowsScenario.SeedAsync(orders, products, links, orderId);

        M2MMaterializationProbe.Reset(scenario.DefaultExcludedTitles, scenario.DefaultExcludedProductIds);
        var single = await orders.GetWithProductsAsync(orderId);
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
        await using var harness = await PostgreSqlTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString, FeatureSchema.ManyToManyPostgreSqlDdl, "m2m");
        var orders = harness.GetRequiredService<M2MOrderStore>();
        var products = harness.GetRequiredService<M2MProductStore>();
        var links = harness.GetRequiredService<M2MOrderProductStore>();
        var orderId = (await orders.InsertAsync(new M2MOrder { Name = "Participating" }))!.Id;
        var scenario = await M2MExcludedRowsScenario.SeedAsync(orders, products, links, orderId);
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
        await using var harness = await PostgreSqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, FeatureSchema.ManyToManyPostgreSqlDdl, "m2m");
        var orders = harness.GetRequiredService<M2MOrderStore>();
        var lonely = (await orders.InsertAsync(new M2MOrder { Name = "Lonely" }))!.Id;

        var loaded = await orders.GetWithProductsAsync(lonely);
        Assert.NotNull(loaded);
        Assert.Empty(loaded!.Products);
    }
}
