using Inquiry;
using Inquiry.DependencyInjection;
using Inquiry.FeatureCatalog;
using Inquiry.SqlServer.DependencyInjection;
using Inquiry.SqlServer.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace Inquiry.SqlServer.Tests;

/// <summary>
/// W6 optimistic concurrency against real SQL Server via the shared <see cref="VersionedItem"/> catalog
/// entity: an ORM-managed version is bumped on a successful update, a stale version makes update/delete a
/// no-op (false by default, throwing when <c>ThrowOnConcurrencyConflict</c> is enabled).
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class ConcurrencyIntegrationTests
{
    private readonly SqlServerContainerFixture _fixture;
    public ConcurrencyIntegrationTests(SqlServerContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task SuccessfulUpdateBumpsVersion()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, FeatureSchema.SqlServerDdl, "conc");
        var store = harness.GetRequiredService<VersionedItemStore>();
        var doc = await store.InsertAsync(new VersionedItem { Title = "v0" });

        doc!.Title = "v1";
        Assert.True(await store.UpdateAsync(doc));

        var reloaded = await store.ByIdAsync(doc.Id);
        Assert.Equal(1, reloaded!.Version);
        Assert.Equal("v1", reloaded.Title);
    }

    [SkippableFact]
    public async Task StaleUpdateReturnsFalseAndDoesNotApply()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, FeatureSchema.SqlServerDdl, "conc");
        var store = harness.GetRequiredService<VersionedItemStore>();
        var stale = await store.InsertAsync(new VersionedItem { Title = "v0" });

        // A concurrent writer advances the row to Version 1.
        var fresh = await store.ByIdAsync(stale!.Id);
        fresh!.Title = "winner";
        Assert.True(await store.UpdateAsync(fresh));

        // The stale copy (still Version 0) must not overwrite the winner.
        stale.Title = "loser";
        Assert.False(await store.UpdateAsync(stale));

        var reloaded = await store.ByIdAsync(stale.Id);
        Assert.Equal("winner", reloaded!.Title);
        Assert.Equal(1, reloaded.Version);
    }

    [SkippableFact]
    public async Task StaleDeleteReturnsFalseAndCurrentDeleteSucceeds()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, FeatureSchema.SqlServerDdl, "conc");
        var store = harness.GetRequiredService<VersionedItemStore>();
        var stale = await store.InsertAsync(new VersionedItem { Title = "v0" });

        var fresh = await store.ByIdAsync(stale!.Id);
        fresh!.Title = "bumped";
        await store.UpdateAsync(fresh); // row now Version 1

        Assert.False(await store.DeleteAsync(stale)); // stale Version 0 — no match
        Assert.NotNull(await store.ByIdAsync(stale.Id));

        var current = await store.ByIdAsync(stale.Id);
        Assert.True(await store.DeleteAsync(current!)); // current Version 1 — deletes
        Assert.Null(await store.ByIdAsync(stale.Id));
    }

    [SkippableFact]
    public async Task StaleUpdateThrowsWhenOptionEnabled()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, FeatureSchema.SqlServerDdl, "concthrow");

        await using var throwing = new ServiceCollection()
            .AddInquiry(o => o.ThrowOnConcurrencyConflict = true)
            .AddInquirySqlServer(harness.ConnectionString)
            .BuildServiceProvider();
        var store = throwing.GetRequiredService<VersionedItemStore>();

        var stale = await store.InsertAsync(new VersionedItem { Title = "v0" });
        var fresh = await store.ByIdAsync(stale!.Id);
        fresh!.Title = "winner";
        await store.UpdateAsync(fresh); // row now Version 1

        stale.Title = "loser";
        await Assert.ThrowsAsync<InquiryConcurrencyException>(() => store.UpdateAsync(stale));
    }
}
