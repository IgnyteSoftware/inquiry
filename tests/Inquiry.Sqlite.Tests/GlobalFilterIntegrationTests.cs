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
    public async Task ParameterizedFilterScopesEveryReadToTheAmbientTenant()
    {
        var harness = await SqliteTestHarness.CreateAsync(FeatureSchema.TenantScopedDocSqliteDdl, "TenantScoped");
        await using var _ = harness;
        var store = harness.GetRequiredService<TenantScopedDocStore>();

        // Writes carry no filter in this release, so seeding needs no scope.
        await store.InsertAsync(new TenantScopedDoc { TenantId = 1, Title = "T1 doc" });
        await store.InsertAsync(new TenantScopedDoc { TenantId = 1, Title = "T1 inactive", IsActive = false });
        await store.InsertAsync(new TenantScopedDoc { TenantId = 2, Title = "T2 doc" });

        long crossTenantId;
        using (InquiryFilterContext.BeginScope(new Dictionary<string, object> { ["TenantId"] = 2L }))
        {
            var t2 = Assert.Single(await store.AllAsync());
            Assert.Equal("T2 doc", t2.Title);
            crossTenantId = t2.Id;
        }

        using (InquiryFilterContext.BeginScope(new Dictionary<string, object> { ["TenantId"] = 1L }))
        {
            // The tenant term and the constant IsActive filter compose: one row, not two.
            var t1 = Assert.Single(await store.AllAsync());
            Assert.Equal("T1 doc", t1.Title);
            Assert.Equal(1L, await store.CountAsync());
            Assert.Single(await store.ByTitleAsync("T1 doc"));

            // A key probe for another tenant's row comes back empty — the filter guards by-key too.
            Assert.Null(await store.ByKeyAsync(crossTenantId));
        }
    }

    [Fact]
    public async Task ParameterizedFilterWithoutAScopeThrowsBeforeExecuting()
    {
        var harness = await SqliteTestHarness.CreateAsync(FeatureSchema.TenantScopedDocSqliteDdl, "TenantScoped");
        await using var _ = harness;
        var store = harness.GetRequiredService<TenantScopedDocStore>();
        await store.InsertAsync(new TenantScopedDoc { TenantId = 1, Title = "Doc" });

        // No ambient scope: the dedicated exception, not an empty result — an empty result here is
        // indistinguishable from working tenant isolation, which is why binding null is forbidden.
        await Assert.ThrowsAsync<InquiryFilterValueMissingException>(() => store.AllAsync());
        await Assert.ThrowsAsync<InquiryFilterValueMissingException>(() => store.CountAsync());

        using (InquiryFilterContext.BeginScope(new Dictionary<string, object> { ["TenantId"] = "wrong-type" }))
        {
            await Assert.ThrowsAsync<InquiryFilterValueMissingException>(() => store.AllAsync());
        }
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
