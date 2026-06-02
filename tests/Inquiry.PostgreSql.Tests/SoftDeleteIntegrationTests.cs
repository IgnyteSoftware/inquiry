using Inquiry.FeatureCatalog;
using Inquiry.PostgreSql.Tests.Fixtures;

namespace Inquiry.PostgreSql.Tests;

/// <summary>
/// W8 soft delete against real PostgreSQL via the shared <see cref="SoftItem"/> catalog entity: a soft
/// delete hides the row from normal selects but keeps it visible via <c>IncludeDeleted</c>, restore brings
/// it back, a hard delete physically removes it, and COUNT respects the active filter.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
public sealed class SoftDeleteIntegrationTests
{
    private readonly PostgreSqlContainerFixture _fixture;
    public SoftDeleteIntegrationTests(PostgreSqlContainerFixture fixture) => _fixture = fixture;

    private async Task<(PostgreSqlTestHarness Harness, SoftItemStore Store, long Id)> SeedOneAsync()
    {
        var harness = await PostgreSqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, FeatureSchema.PostgreSqlDdl, "soft");
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
}
