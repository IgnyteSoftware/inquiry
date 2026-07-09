using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Inquiry.Entities;
using Inquiry.Sqlite.Tests.Fixtures;
using Inquiry.Stores;

namespace Inquiry.Sqlite.Tests;

[InquiryTable("GroupItem")]
public sealed class GroupItem
{
    [InquiryKey(IsGenerated = true)]
    public long Id { get; set; }

    [InquiryColumn("Category")]
    public string Category { get; set; } = string.Empty;

    [InquiryColumn("Priority")]
    public int Priority { get; set; }
}

public partial class GroupItemStore : InquiryStore<GroupItem>
{
    [InquiryInsert]
    public partial Task<int> InsertAsync(GroupItem item, CancellationToken cancellationToken = default);

    [InquiryGroupCount("Category")]
    public partial Task<IReadOnlyList<GroupCount<string>>> CountByCategoryAsync(CancellationToken cancellationToken = default);

    [InquiryGroupCount("Priority")]
    public partial Task<IReadOnlyList<GroupCount<int>>> CountByPriorityAsync(CancellationToken cancellationToken = default);
}

public sealed class GroupCountIntegrationTests
{
    private const string Ddl = "CREATE TABLE GroupItem (Id INTEGER PRIMARY KEY AUTOINCREMENT, Category TEXT NOT NULL, Priority INTEGER NOT NULL);";

    [Fact]
    public async Task CountByCategoryReturnsGroupedCounts()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Ddl, "GroupCount");
        var store = harness.GetRequiredService<GroupItemStore>();

        await store.InsertAsync(new GroupItem { Category = "A", Priority = 1 });
        await store.InsertAsync(new GroupItem { Category = "A", Priority = 2 });
        await store.InsertAsync(new GroupItem { Category = "B", Priority = 1 });
        await store.InsertAsync(new GroupItem { Category = "A", Priority = 3 });

        var counts = await store.CountByCategoryAsync();
        Assert.Equal(2, counts.Count);

        var a = counts.Single(c => c.Key == "A");
        Assert.Equal(3, a.Count);

        var b = counts.Single(c => c.Key == "B");
        Assert.Equal(1, b.Count);
    }

    [Fact]
    public async Task CountByPriorityReturnsGroupedCounts()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Ddl, "GroupCount");
        var store = harness.GetRequiredService<GroupItemStore>();

        await store.InsertAsync(new GroupItem { Category = "A", Priority = 1 });
        await store.InsertAsync(new GroupItem { Category = "B", Priority = 1 });
        await store.InsertAsync(new GroupItem { Category = "C", Priority = 2 });

        var counts = await store.CountByPriorityAsync();
        Assert.Equal(2, counts.Count);

        var p1 = counts.Single(c => c.Key == 1);
        Assert.Equal(2, p1.Count);

        var p2 = counts.Single(c => c.Key == 2);
        Assert.Equal(1, p2.Count);
    }

    [Fact]
    public async Task CountByCategoryReturnsEmptyForEmptyTable()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Ddl, "GroupCount");
        var store = harness.GetRequiredService<GroupItemStore>();

        var counts = await store.CountByCategoryAsync();
        Assert.Empty(counts);
    }
}
