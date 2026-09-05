using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Inquiry.Entities;
using Inquiry.Sqlite.Tests.Fixtures;
using Inquiry.Stores;

namespace Inquiry.Sqlite.Tests;

[InquiryTable("ExistsWidget")]
public sealed class ExistsWidget
{
    [InquiryKey(IsGenerated = true)]
    public long Id { get; set; }

    [InquiryColumn("Name")]
    public string Name { get; set; } = string.Empty;

    [InquiryColumn("IsDeleted"), InquirySoftDelete]
    public bool IsDeleted { get; set; }
}

public partial class ExistsWidgetStore : InquiryStore<ExistsWidget>
{
    [InquiryInsert]
    public partial Task<ExistsWidget?> InsertAsync(ExistsWidget widget, CancellationToken cancellationToken = default);

    [InquiryDelete]
    public partial Task<bool> SoftDeleteAsync(long id, CancellationToken cancellationToken = default);

    [InquiryExists]
    public partial Task<bool> AnyAsync(CancellationToken cancellationToken = default);

    [InquiryExists]
    [InquiryWhere("Name")]
    public partial Task<bool> ExistsByNameAsync(string name, CancellationToken cancellationToken = default);
}

/// <summary>
/// End-to-end [InquiryExists] against real SQLite: the EXISTS scalar round-trips to a bool, criteria
/// test for a matching row, and the active-row filter excludes soft-deleted rows.
/// </summary>
public sealed class ExistsIntegrationTests
{
    private const string Ddl =
        "CREATE TABLE ExistsWidget (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL, IsDeleted INTEGER NOT NULL DEFAULT 0);";

    [Fact]
    public async Task AnyReflectsWhetherTableHasRows()
    {
        var harness = await SqliteTestHarness.CreateAsync(Ddl, "Exists");
        await using var _ = harness;
        var store = harness.GetRequiredService<ExistsWidgetStore>();

        Assert.False(await store.AnyAsync());
        await store.InsertAsync(new ExistsWidget { Name = "Alpha" });
        Assert.True(await store.AnyAsync());
    }

    [Fact]
    public async Task ExistsByNameTestsForAMatch()
    {
        var harness = await SqliteTestHarness.CreateAsync(Ddl, "Exists");
        await using var _ = harness;
        var store = harness.GetRequiredService<ExistsWidgetStore>();
        await store.InsertAsync(new ExistsWidget { Name = "Alpha" });

        Assert.True(await store.ExistsByNameAsync("Alpha"));
        Assert.False(await store.ExistsByNameAsync("Beta"));
    }

    [Fact]
    public async Task ExistsExcludesSoftDeletedRows()
    {
        var harness = await SqliteTestHarness.CreateAsync(Ddl, "Exists");
        await using var _ = harness;
        var store = harness.GetRequiredService<ExistsWidgetStore>();
        var inserted = await store.InsertAsync(new ExistsWidget { Name = "Alpha" });

        Assert.True(await store.ExistsByNameAsync("Alpha"));
        await store.SoftDeleteAsync(inserted!.Id);
        // The soft-deleted row no longer "exists" for the active-row-filtered test.
        Assert.False(await store.ExistsByNameAsync("Alpha"));
        Assert.False(await store.AnyAsync());
    }
}
