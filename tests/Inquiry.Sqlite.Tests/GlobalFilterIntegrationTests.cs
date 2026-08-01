using System.Linq;
using System.Threading.Tasks;
using Inquiry.FeatureCatalog;
using Inquiry.Sqlite.Tests.Fixtures;

namespace Inquiry.Sqlite.Tests;

/// <summary>
/// End-to-end [InquiryGlobalFilter] behaviour against real SQLite via the shared
/// <see cref="GlobalFilterDoc"/> and <see cref="GlobalFilterTicket"/> catalog entities: a global
/// filter hides non-matching rows from every select, coexists with soft delete, survives
/// <c>IncludeDeleted</c> (which only drops the soft-delete term), and <c>KeepWhen = false</c>
/// inverts the kept value.
/// </summary>
public sealed class GlobalFilterIntegrationTests
{
    [Fact]
    public async Task GlobalFilterHidesUnpublishedRowsFromEverySelect()
    {
        var harness = await SqliteTestHarness.CreateAsync(FeatureSchema.GlobalFilterSqliteDdl, "GlobalFilter");
        await using var _ = harness;
        var store = harness.GetRequiredService<GlobalFilterDocStore>();

        await store.InsertAsync(new GlobalFilterDoc { Name = "Published", IsPublished = true });
        await store.InsertAsync(new GlobalFilterDoc { Name = "Draft", IsPublished = false });

        var visible = Assert.Single(await store.AllAsync());
        Assert.Equal("Published", visible.Name);
        Assert.Equal(1L, await store.CountPublishedAsync());
    }

    [Fact]
    public async Task IgnoreFilterBypassesOnlyTheNamedGateAndOnlyOnTheAnnotatedMethods()
    {
        var harness = await SqliteTestHarness.CreateAsync(FeatureSchema.GlobalFilterSqliteDdl, "GlobalFilter");
        await using var _ = harness;
        var store = harness.GetRequiredService<GlobalFilterDocStore>();

        await store.InsertAsync(new GlobalFilterDoc { Name = "Published", IsPublished = true });
        await store.InsertAsync(new GlobalFilterDoc { Name = "Draft", IsPublished = false });
        // A deleted draft: the bypass drops ONLY the publish gate, so soft delete still hides it.
        await store.InsertAsync(new GlobalFilterDoc { Name = "DeletedDraft", IsPublished = false, IsDeleted = true });

        // The bypass methods see the live draft but not the deleted one.
        var drafts = await store.AllIncludingDraftsAsync();
        Assert.Equal(new[] { "Draft", "Published" }, drafts.Select(d => d.Name).OrderBy(n => n).ToArray());
        Assert.Equal(2L, await store.CountIncludingDraftsAsync());

        // The unannotated methods still filter — the bypass is per method, not per store.
        var published = Assert.Single(await store.AllAsync());
        Assert.Equal("Published", published.Name);
        Assert.Equal(1L, await store.CountPublishedAsync());
    }

    [Fact]
    public async Task IncludeDeletedKeepsTheGlobalFilter()
    {
        var harness = await SqliteTestHarness.CreateAsync(FeatureSchema.GlobalFilterSqliteDdl, "GlobalFilter");
        await using var _ = harness;
        var store = harness.GetRequiredService<GlobalFilterDocStore>();

        await store.InsertAsync(new GlobalFilterDoc { Name = "PublishedActive", IsPublished = true, IsDeleted = false });
        await store.InsertAsync(new GlobalFilterDoc { Name = "PublishedDeleted", IsPublished = true, IsDeleted = true });
        await store.InsertAsync(new GlobalFilterDoc { Name = "DraftDeleted", IsPublished = false, IsDeleted = true });

        // Default select: published AND not-deleted.
        var active = Assert.Single(await store.AllAsync());
        Assert.Equal("PublishedActive", active.Name);

        // IncludeDeleted: soft-delete term dropped, but the publish filter remains — so the unpublished
        // (draft) deleted row stays hidden while both published rows surface.
        var includingDeleted = await store.AllIncludingDeletedAsync();
        Assert.Equal(
            new[] { "PublishedActive", "PublishedDeleted" },
            includingDeleted.Select(d => d.Name).OrderBy(n => n).ToArray());
    }

    [Fact]
    public async Task KeepWhenFalseKeepsUnarchivedRows()
    {
        var harness = await SqliteTestHarness.CreateAsync(FeatureSchema.GlobalFilterSqliteDdl, "GlobalFilter");
        await using var _ = harness;
        var store = harness.GetRequiredService<GlobalFilterTicketStore>();

        await store.InsertAsync(new GlobalFilterTicket { Title = "Open", IsArchived = false });
        await store.InsertAsync(new GlobalFilterTicket { Title = "Archived", IsArchived = true });

        var visible = Assert.Single(await store.AllAsync());
        Assert.Equal("Open", visible.Title);
    }
}
