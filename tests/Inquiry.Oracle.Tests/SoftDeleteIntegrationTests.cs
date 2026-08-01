using Inquiry.FeatureCatalog;
using Inquiry.Oracle.Tests.Fixtures;

namespace Inquiry.Oracle.Tests;

/// <summary>
/// Soft delete against real Oracle via the shared <see cref="SoftItem"/> catalog entity: a soft
/// delete hides the row from normal selects but keeps it visible via <c>IncludeDeleted</c>, restore brings
/// it back, a hard delete physically removes it, and COUNT respects the active filter.
/// </summary>
[Collection(OracleCollection.Name)]
public sealed class SoftDeleteIntegrationTests
{
    private readonly OracleContainerFixture _fixture;
    public SoftDeleteIntegrationTests(OracleContainerFixture fixture) => _fixture = fixture;

    private async Task<(OracleTestHarness Harness, SoftItemStore Store, long Id)> SeedOneAsync()
    {
        var harness = await OracleTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, FeatureSchema.OracleDdl, "soft");
        var store = harness.GetRequiredService<SoftItemStore>();
        var inserted = await store.InsertAsync(new SoftItem { Name = "Alpha" });
        return (harness, store, inserted!.Id);
    }

    [SkippableFact]
    public async Task SoftDeleteHidesFromSelectsButIncludeDeletedSeesIt()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        var (harness, store, id) = await SeedOneAsync();
        await using var _ = harness;

        Assert.True(await store.SoftDeleteAsync(id));

        Assert.Empty(await store.AllAsync());
        Assert.Null(await store.ByIdAsync(id));

        var all = await store.AllIncludingDeletedAsync();
        var only = Assert.Single(all);
        Assert.True(only.IsDeleted);
    }

    [SkippableFact]
    public async Task RestoreBringsTheRowBack()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        var (harness, store, id) = await SeedOneAsync();
        await using var _ = harness;

        await store.SoftDeleteAsync(id);
        Assert.True(await store.RestoreAsync(id));

        var only = Assert.Single(await store.AllAsync());
        Assert.False(only.IsDeleted);
        Assert.Equal(id, only.Id);
    }

    [SkippableFact]
    public async Task CountExcludesSoftDeletedRows()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        var (harness, store, id) = await SeedOneAsync();
        await using var _ = harness;
        await store.InsertAsync(new SoftItem { Name = "Beta" });

        Assert.Equal(2L, await store.CountActiveAsync());

        await store.SoftDeleteAsync(id);
        Assert.Equal(1L, await store.CountActiveAsync());
    }

    [SkippableFact]
    public async Task HardDeletePhysicallyRemovesTheRow()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        var (harness, store, id) = await SeedOneAsync();
        await using var _ = harness;

        Assert.True(await store.PurgeAsync(id));

        Assert.Empty(await store.AllIncludingDeletedAsync());
    }

    [SkippableFact]
    public async Task ProjectionExcludesSoftDeletedRowsButIncludeDeletedSeesThem()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        var (harness, store, id) = await SeedOneAsync();
        await using var _ = harness;
        await store.InsertAsync(new SoftItem { Name = "Beta" });

        // Both rows visible through the projection before any delete.
        Assert.Equal(2, (await store.NamesAsync()).Count);

        await store.SoftDeleteAsync(id);

        // The soft-deleted row is hidden from the projection — the soft-delete filter is composed.
        var onlyActive = Assert.Single(await store.NamesAsync());
        Assert.Equal("Beta", onlyActive.Name);

        // IncludeDeleted projection sees both rows again.
        Assert.Equal(2, (await store.NamesIncludingDeletedAsync()).Count);
    }
}
