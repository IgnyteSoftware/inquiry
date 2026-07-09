using System.Linq;
using Inquiry.FeatureCatalog;
using Inquiry.SqlServer.Tests.Fixtures;

namespace Inquiry.SqlServer.Tests;

/// <summary>
/// Global query filter against real SQL Server via the shared <see cref="GlobalFilterDoc"/> and
/// <see cref="GlobalFilterTicket"/> catalog entities: a publish gate hides unpublished rows from
/// every select, coexists with soft delete, survives <c>IncludeDeleted</c>, and
/// <c>KeepWhen = false</c> inverts the kept value.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class GlobalFilterIntegrationTests
{
    private readonly SqlServerContainerFixture _fixture;
    public GlobalFilterIntegrationTests(SqlServerContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task GlobalFilterHidesUnpublishedRowsFromEverySelect()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, FeatureSchema.GlobalFilterSqlServerDdl, "gf");
        var store = harness.GetRequiredService<GlobalFilterDocStore>();

        await store.InsertAsync(new GlobalFilterDoc { Name = "Published", IsPublished = true });
        await store.InsertAsync(new GlobalFilterDoc { Name = "Draft", IsPublished = false });

        var visible = Assert.Single(await store.AllAsync());
        Assert.Equal("Published", visible.Name);
        Assert.Equal(1L, await store.CountPublishedAsync());
    }

    [SkippableFact]
    public async Task IncludeDeletedKeepsTheGlobalFilter()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, FeatureSchema.GlobalFilterSqlServerDdl, "gf");
        var store = harness.GetRequiredService<GlobalFilterDocStore>();

        await store.InsertAsync(new GlobalFilterDoc { Name = "PublishedActive", IsPublished = true, IsDeleted = false });
        await store.InsertAsync(new GlobalFilterDoc { Name = "PublishedDeleted", IsPublished = true, IsDeleted = true });
        await store.InsertAsync(new GlobalFilterDoc { Name = "DraftDeleted", IsPublished = false, IsDeleted = true });

        var active = Assert.Single(await store.AllAsync());
        Assert.Equal("PublishedActive", active.Name);

        var includingDeleted = await store.AllIncludingDeletedAsync();
        Assert.Equal(
            new[] { "PublishedActive", "PublishedDeleted" },
            includingDeleted.Select(d => d.Name).OrderBy(n => n).ToArray());
    }

    [SkippableFact]
    public async Task KeepWhenFalseKeepsUnarchivedRows()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, FeatureSchema.GlobalFilterSqlServerDdl, "gf");
        var store = harness.GetRequiredService<GlobalFilterTicketStore>();

        await store.InsertAsync(new GlobalFilterTicket { Title = "Open", IsArchived = false });
        await store.InsertAsync(new GlobalFilterTicket { Title = "Archived", IsArchived = true });

        var visible = Assert.Single(await store.AllAsync());
        Assert.Equal("Open", visible.Title);
    }
}
