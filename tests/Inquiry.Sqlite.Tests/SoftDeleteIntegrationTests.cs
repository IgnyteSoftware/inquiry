using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Inquiry.Entities;
using Inquiry.Sqlite.Tests.Fixtures;
using Inquiry.Stores;

namespace Inquiry.Sqlite.Tests;

[InquiryTable("SoftDeleteWidget")]
public sealed class SoftDeleteWidget
{
    [InquiryKey(IsGenerated = true)]
    public long Id { get; set; }

    [InquiryColumn("Name")]
    public string Name { get; set; } = string.Empty;

    [InquiryColumn("IsDeleted"), InquirySoftDelete]
    public bool IsDeleted { get; set; }
}

public partial class SoftDeleteWidgetStore : InquiryStore<SoftDeleteWidget>
{
    [InquiryInsert(ReturnEntity = true)]
    public partial Task<SoftDeleteWidget?> InsertAsync(SoftDeleteWidget widget, CancellationToken cancellationToken = default);

    [InquirySelectAll]
    public partial Task<IReadOnlyList<SoftDeleteWidget>> AllAsync(CancellationToken cancellationToken = default);

    [InquirySelectAll(IncludeDeleted = true)]
    public partial Task<IReadOnlyList<SoftDeleteWidget>> AllIncludingDeletedAsync(CancellationToken cancellationToken = default);

    [InquirySelectOneByKey]
    public partial Task<SoftDeleteWidget?> ByIdAsync(long id, CancellationToken cancellationToken = default);

    [InquiryDeleteOneByKey]
    public partial Task<bool> SoftDeleteAsync(long id, CancellationToken cancellationToken = default);

    [InquiryDeleteOneByKey(HardDelete = true)]
    public partial Task<bool> PurgeAsync(long id, CancellationToken cancellationToken = default);

    [InquiryRestoreOneByKey]
    public partial Task<bool> RestoreAsync(long id, CancellationToken cancellationToken = default);

    [InquiryCount]
    public partial Task<long> CountActiveAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// End-to-end soft-delete behaviour against real SQLite: a soft delete hides the row from normal
/// selects but keeps it visible via <c>IncludeDeleted</c>, restore brings it back, and a hard delete
/// physically removes it.
/// </summary>
public sealed class SoftDeleteIntegrationTests
{
    private const string Ddl =
        "CREATE TABLE SoftDeleteWidget (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL, IsDeleted INTEGER NOT NULL DEFAULT 0);";

    private static async Task<(SqliteTestHarness Harness, SoftDeleteWidgetStore Store, long Id)> SeedOneAsync()
    {
        var harness = await SqliteTestHarness.CreateAsync(Ddl, "SoftDelete");
        var store = harness.GetRequiredService<SoftDeleteWidgetStore>();
        var inserted = await store.InsertAsync(new SoftDeleteWidget { Name = "Alpha" });
        return (harness, store, inserted!.Id);
    }

    [Fact]
    public async Task SoftDeleteHidesFromSelectsButIncludeDeletedSeesIt()
    {
        var (harness, store, id) = await SeedOneAsync();
        await using var _ = harness;

        Assert.True(await store.SoftDeleteAsync(id));

        Assert.Empty(await store.AllAsync());
        Assert.Null(await store.ByIdAsync(id));

        var all = await store.AllIncludingDeletedAsync();
        var only = Assert.Single(all);
        Assert.True(only.IsDeleted);
    }

    [Fact]
    public async Task RestoreBringsTheRowBack()
    {
        var (harness, store, id) = await SeedOneAsync();
        await using var _ = harness;

        await store.SoftDeleteAsync(id);
        Assert.True(await store.RestoreAsync(id));

        var only = Assert.Single(await store.AllAsync());
        Assert.False(only.IsDeleted);
        Assert.Equal(id, only.Id);
    }

    [Fact]
    public async Task CountExcludesSoftDeletedRows()
    {
        var (harness, store, id) = await SeedOneAsync();
        await using var _ = harness;
        await store.InsertAsync(new SoftDeleteWidget { Name = "Beta" });

        Assert.Equal(2L, await store.CountActiveAsync());

        await store.SoftDeleteAsync(id);
        Assert.Equal(1L, await store.CountActiveAsync());
    }

    [Fact]
    public async Task HardDeletePhysicallyRemovesTheRow()
    {
        var (harness, store, id) = await SeedOneAsync();
        await using var _ = harness;

        Assert.True(await store.PurgeAsync(id));

        Assert.Empty(await store.AllIncludingDeletedAsync());
    }
}
