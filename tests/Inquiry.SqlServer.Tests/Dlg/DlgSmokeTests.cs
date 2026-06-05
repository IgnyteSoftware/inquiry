using Inquiry.Benchmarks.DLG;
using Xunit;

namespace Inquiry.SqlServer.Tests.Dlg;

/// <summary>
/// Proves DlgSetup (procs + primed config) works and each Phase-1 DLG capability returns correct
/// results against a real SQL Server. All tests share one database (DLG's config is process-static).
/// </summary>
[Collection(DlgCollection.Name)]
public sealed class DlgSmokeTests
{
    private readonly DlgDatabaseFixture _fixture;
    public DlgSmokeTests(DlgDatabaseFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task SelectAll_ReturnsAtLeastSeededShippers()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);

        var shippers = await Shipper.SelectAllAsync();

        Assert.True(shippers.Count >= DlgDatabaseFixture.SeededShippers);
    }

    [SkippableFact]
    public async Task SelectByKey_ReturnsShipper()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);

        var shipper = await Shipper.SelectOneAsync(1);

        Assert.NotNull(shipper);
        Assert.Equal(1, shipper!.ShipperID);
    }

    [SkippableFact]
    public async Task Insert_AddsRow_FoundBySelectByField()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        var unique = "Ins-" + Guid.NewGuid().ToString("N")[..8];

        var ok = await new Shipper { CompanyName = unique, Phone = "555-7777" }.InsertAsync();
        Assert.True(ok);

        var found = await Shipper.SelectByFieldAsync(ShipperFields.CompanyName, unique);
        Assert.Single(found);
    }

    [SkippableFact]
    public async Task Update_ChangesRow()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        var unique = "Upd-" + Guid.NewGuid().ToString("N")[..8];

        var entity = new Shipper { CompanyName = unique, Phone = "555-0001" };
        await entity.InsertAsync(getBackValues: true);
        Assert.True(entity.ShipperID > 0);

        entity.Phone = "555-0002";
        Assert.True(await entity.UpdateAsync());

        var reloaded = await Shipper.SelectOneAsync(entity.ShipperID);
        Assert.Equal("555-0002", reloaded!.Phone);
    }

    [SkippableFact]
    public async Task Upsert_OnExistingKey_Updates()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        var unique = "Ups-" + Guid.NewGuid().ToString("N")[..8];

        var entity = new Shipper { CompanyName = "seed", Phone = "x" };
        await entity.InsertAsync(getBackValues: true);

        // Set the identity key first, then TakeSnapshot() so DLG's dirty-tracker sees only the
        // subsequent field mutations (CompanyName, Phone) — not ShipperID, which is an identity
        // column the Update proc cannot write.
        var changed = new Shipper();
        changed.ShipperID = entity.ShipperID;
        changed.TakeSnapshot();
        changed.CompanyName = unique;
        changed.Phone = "555-0003";

        Assert.True(await changed.UpsertAsync());

        var reloaded = await Shipper.SelectOneAsync(entity.ShipperID);
        Assert.Equal(unique, reloaded!.CompanyName);
    }

    [SkippableFact]
    public async Task Count_ReturnsSeededProducts()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);

        var count = await Product.SelectAllCountAsync();

        Assert.Equal(DlgDatabaseFixture.SeededProducts, count);
    }

    [SkippableFact]
    public async Task OffsetPage_ReturnsPageSizedSlice()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);

        var page = await Product.SelectAllPagedAsync(pageNumber: 1, pageSize: 2, orderByStatement: "ProductID");

        Assert.Equal(2, page.Count);
    }

    [SkippableFact]
    public async Task Search_Like_FindsSeededProducts()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);

        var matches = await Product.SelectByFieldAsync(
            ProductFields.ProductName, "%Product%", null, TypeOperation.Like);

        Assert.Equal(DlgDatabaseFixture.SeededProducts, matches.Count);
    }

    [SkippableFact]
    public async Task Eager_LoadsCategoryWithProducts()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        var catId = _fixture.FirstCategoryId;
        var expected = _fixture.ProductCountByCategoryId[catId];

        var category = await Category.SelectOneWithProductsUsingCategoryIDAsync(catId);

        Assert.NotNull(category);
        // Child collection navigation property — confirmed as ProductsUsingCategoryID in CategoryBase.cs line 175.
        Assert.Equal(expected, category!.ProductsUsingCategoryID!.Count);
    }
}
