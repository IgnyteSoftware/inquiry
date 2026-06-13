using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Inquiry.Entities;
using Inquiry.Sqlite.Tests.Fixtures;
using Inquiry.Stores;

namespace Inquiry.Sqlite.Tests;

[InquiryTable("GlobalFilterDoc")]
public sealed class GlobalFilterDoc
{
    [InquiryKey(IsGenerated = true)]
    public long Id { get; set; }

    [InquiryColumn("Name")]
    public string Name { get; set; } = string.Empty;

    [InquiryColumn("IsPublished"), InquiryGlobalFilter]
    public bool IsPublished { get; set; }

    [InquiryColumn("IsDeleted"), InquirySoftDelete]
    public bool IsDeleted { get; set; }
}

public partial class GlobalFilterDocStore : InquiryStore<GlobalFilterDoc>
{
    [InquiryInsert(ReturnEntity = true)]
    public partial Task<GlobalFilterDoc?> InsertAsync(GlobalFilterDoc doc, CancellationToken cancellationToken = default);

    [InquirySelectAll]
    public partial Task<IReadOnlyList<GlobalFilterDoc>> AllAsync(CancellationToken cancellationToken = default);

    // IncludeDeleted drops the soft-delete filter but the global publish filter still applies.
    [InquirySelectAll(IncludeDeleted = true)]
    public partial Task<IReadOnlyList<GlobalFilterDoc>> AllIncludingDeletedAsync(CancellationToken cancellationToken = default);

    [InquiryCount]
    public partial Task<long> CountPublishedAsync(CancellationToken cancellationToken = default);
}

[InquiryTable("GlobalFilterTicket")]
public sealed class GlobalFilterTicket
{
    [InquiryKey(IsGenerated = true)]
    public long Id { get; set; }

    [InquiryColumn("Title")]
    public string Title { get; set; } = string.Empty;

    // KeepWhen = false: keep the rows where the flag is false (unarchived).
    [InquiryColumn("IsArchived"), InquiryGlobalFilter(KeepWhen = false)]
    public bool IsArchived { get; set; }
}

public partial class GlobalFilterTicketStore : InquiryStore<GlobalFilterTicket>
{
    [InquiryInsert(ReturnEntity = true)]
    public partial Task<GlobalFilterTicket?> InsertAsync(GlobalFilterTicket ticket, CancellationToken cancellationToken = default);

    [InquirySelectAll]
    public partial Task<IReadOnlyList<GlobalFilterTicket>> AllAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// End-to-end [InquiryGlobalFilter] behaviour against real SQLite: a global filter hides non-matching
/// rows from every select, coexists with soft delete, survives <c>IncludeDeleted</c> (which only drops
/// the soft-delete term), and <c>KeepWhen = false</c> inverts the kept value.
/// </summary>
public sealed class GlobalFilterIntegrationTests
{
    private const string DocDdl =
        "CREATE TABLE GlobalFilterDoc (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL, IsPublished INTEGER NOT NULL DEFAULT 0, IsDeleted INTEGER NOT NULL DEFAULT 0);";

    private const string TicketDdl =
        "CREATE TABLE GlobalFilterTicket (Id INTEGER PRIMARY KEY AUTOINCREMENT, Title TEXT NOT NULL, IsArchived INTEGER NOT NULL DEFAULT 0);";

    [Fact]
    public async Task GlobalFilterHidesUnpublishedRowsFromEverySelect()
    {
        var harness = await SqliteTestHarness.CreateAsync(DocDdl, "GlobalFilter");
        await using var _ = harness;
        var store = harness.GetRequiredService<GlobalFilterDocStore>();

        await store.InsertAsync(new GlobalFilterDoc { Name = "Published", IsPublished = true });
        await store.InsertAsync(new GlobalFilterDoc { Name = "Draft", IsPublished = false });

        var visible = Assert.Single(await store.AllAsync());
        Assert.Equal("Published", visible.Name);
        Assert.Equal(1L, await store.CountPublishedAsync());
    }

    [Fact]
    public async Task IncludeDeletedKeepsTheGlobalFilter()
    {
        var harness = await SqliteTestHarness.CreateAsync(DocDdl, "GlobalFilter");
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
        var harness = await SqliteTestHarness.CreateAsync(TicketDdl, "GlobalFilter");
        await using var _ = harness;
        var store = harness.GetRequiredService<GlobalFilterTicketStore>();

        await store.InsertAsync(new GlobalFilterTicket { Title = "Open", IsArchived = false });
        await store.InsertAsync(new GlobalFilterTicket { Title = "Archived", IsArchived = true });

        var visible = Assert.Single(await store.AllAsync());
        Assert.Equal("Open", visible.Title);
    }
}
