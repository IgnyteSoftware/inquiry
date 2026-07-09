using System.Linq;
using Inquiry.FeatureCatalog;
using Inquiry.Oracle.Tests.Fixtures;

namespace Inquiry.Oracle.Tests;

[Collection(OracleCollection.Name)]
public sealed class ManyToManyIntegrationTests
{
    private readonly OracleContainerFixture _fixture;
    public ManyToManyIntegrationTests(OracleContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task SingleEagerLoadsRelatedRowsThroughJunction()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString, FeatureSchema.ManyToManyOracleDdl, "m2m");

        var orders = harness.GetRequiredService<M2MOrderStore>();
        var products = harness.GetRequiredService<M2MProductStore>();
        var links = harness.GetRequiredService<M2MOrderProductStore>();

        var order1 = (await orders.InsertAsync(new M2MOrder { Name = "Order1" }))!;
        var order2 = (await orders.InsertAsync(new M2MOrder { Name = "Order2" }))!;
        var apple = (await products.InsertAsync(new M2MProduct { Title = "Apple" }))!;
        var banana = (await products.InsertAsync(new M2MProduct { Title = "Banana" }))!;
        var cherry = (await products.InsertAsync(new M2MProduct { Title = "Cherry" }))!;

        await links.LinkAsync(new M2MOrderProduct { OrderId = order1.Id, ProductId = apple.Id });
        await links.LinkAsync(new M2MOrderProduct { OrderId = order1.Id, ProductId = banana.Id });
        await links.LinkAsync(new M2MOrderProduct { OrderId = order2.Id, ProductId = banana.Id });
        await links.LinkAsync(new M2MOrderProduct { OrderId = order2.Id, ProductId = cherry.Id });

        var loaded = await orders.GetWithProductsAsync(order1.Id);

        Assert.NotNull(loaded);
        Assert.Equal(new[] { "Apple", "Banana" }, loaded.Products.Select(p => p.Title).OrderBy(t => t));
    }

    [SkippableFact]
    public async Task AllEagerAssemblesEveryParentsCollection()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString, FeatureSchema.ManyToManyOracleDdl, "m2m");

        var orders = harness.GetRequiredService<M2MOrderStore>();
        var products = harness.GetRequiredService<M2MProductStore>();
        var links = harness.GetRequiredService<M2MOrderProductStore>();

        var order1 = (await orders.InsertAsync(new M2MOrder { Name = "Order1" }))!;
        var order2 = (await orders.InsertAsync(new M2MOrder { Name = "Order2" }))!;
        var apple = (await products.InsertAsync(new M2MProduct { Title = "Apple" }))!;
        var banana = (await products.InsertAsync(new M2MProduct { Title = "Banana" }))!;
        var cherry = (await products.InsertAsync(new M2MProduct { Title = "Cherry" }))!;

        await links.LinkAsync(new M2MOrderProduct { OrderId = order1.Id, ProductId = apple.Id });
        await links.LinkAsync(new M2MOrderProduct { OrderId = order1.Id, ProductId = banana.Id });
        await links.LinkAsync(new M2MOrderProduct { OrderId = order2.Id, ProductId = banana.Id });
        await links.LinkAsync(new M2MOrderProduct { OrderId = order2.Id, ProductId = cherry.Id });

        var all = await orders.AllWithProductsAsync().ToListAsync();

        Assert.Equal(2, all.Count);
        var first = all.OrderBy(o => o.Name).First();
        var second = all.OrderBy(o => o.Name).Last();
        Assert.Equal(new[] { "Apple", "Banana" }, first.Products.Select(p => p.Title).OrderBy(t => t));
        Assert.Equal(new[] { "Banana", "Cherry" }, second.Products.Select(p => p.Title).OrderBy(t => t));
    }

    [SkippableFact]
    public async Task EagerCollectionIsEmptyWhenNoAssociations()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString, FeatureSchema.ManyToManyOracleDdl, "m2m");

        var orders = harness.GetRequiredService<M2MOrderStore>();

        var order = (await orders.InsertAsync(new M2MOrder { Name = "Lonely" }))!;
        var loaded = await orders.GetWithProductsAsync(order.Id);

        Assert.NotNull(loaded);
        Assert.Empty(loaded.Products);
    }
}
