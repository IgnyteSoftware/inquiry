using Inquiry.Entities;
using Inquiry.Sqlite.Tests.Fixtures;
using Inquiry.Stores;

namespace Inquiry.Sqlite.Tests;

[InquiryTable("ExcludeAnnouncement")]
public sealed class ExcludeAnnouncement
{
    [InquiryKey]
    public int Id { get; set; }

    [InquiryColumn("Title")]
    public string Title { get; set; } = string.Empty;

    [InquiryColumn("Image")]
    public byte[]? Image { get; set; }

    [InquiryColumn("Caption")]
    public string Caption { get; set; } = "not read";
}

public partial class ExcludeAnnouncementStore : InquiryStore<ExcludeAnnouncement>
{
    [InquiryInsert]
    public partial Task<int> InsertAsync(ExcludeAnnouncement announcement, CancellationToken cancellationToken = default);

    [InquirySelectAll(OrderBy = "Id", Exclude = new[] { nameof(ExcludeAnnouncement.Image), nameof(ExcludeAnnouncement.Caption) })]
    public partial Task<IReadOnlyList<ExcludeAnnouncement>> ListAsync(CancellationToken cancellationToken = default);

    [InquirySelectOneByKey(Exclude = new[] { nameof(ExcludeAnnouncement.Image) })]
    public partial Task<ExcludeAnnouncement?> GetAsync(int id, CancellationToken cancellationToken = default);

    [InquirySelectAllByField("Title", Exclude = new[] { nameof(ExcludeAnnouncement.Image) })]
    public partial IAsyncEnumerable<ExcludeAnnouncement> ByTitleAsync(string title, CancellationToken cancellationToken = default);

    [InquirySelectAllByPredicate(Exclude = new[] { nameof(ExcludeAnnouncement.Image) })]
    [InquiryWhere("Id", Compare.GreaterThan)]
    public partial Task<IReadOnlyList<ExcludeAnnouncement>> AfterAsync(int id, CancellationToken cancellationToken = default);

    [InquirySelectOneByKey]
    public partial Task<ExcludeAnnouncement?> GetFullAsync(int id, CancellationToken cancellationToken = default);
}

/// <summary>
/// Selects with <c>Exclude</c> leave the named columns out of the SQL and return the entity with
/// those properties at their initial values, while a full read of the same row still returns them.
/// </summary>
public sealed class SelectExcludeIntegrationTests
{
    private const string Ddl =
        "CREATE TABLE ExcludeAnnouncement (Id INTEGER PRIMARY KEY, Title TEXT NOT NULL, Image BLOB NULL, Caption TEXT NOT NULL);";

    [Fact]
    public async Task ExcludedColumnsAreNotReadAndKeepTheirInitialValues()
    {
        var harness = await SqliteTestHarness.CreateAsync(Ddl, "SelectExclude");
        await using var _ = harness;
        var store = harness.GetRequiredService<ExcludeAnnouncementStore>();
        await store.InsertAsync(new ExcludeAnnouncement { Id = 1, Title = "First", Image = [1, 2, 3], Caption = "one" });
        await store.InsertAsync(new ExcludeAnnouncement { Id = 2, Title = "Second", Image = [4, 5], Caption = "two" });

        var listed = await store.ListAsync();
        Assert.Equal(["First", "Second"], listed.Select(static a => a.Title));
        Assert.All(listed, static a => Assert.Null(a.Image));
        Assert.All(listed, static a => Assert.Equal("not read", a.Caption));

        var one = Assert.IsType<ExcludeAnnouncement>(await store.GetAsync(1));
        Assert.Null(one.Image);
        Assert.Equal("one", one.Caption);

        var byTitle = new List<ExcludeAnnouncement>();
        await foreach (var item in store.ByTitleAsync("Second"))
        {
            byTitle.Add(item);
        }

        Assert.Null(Assert.Single(byTitle).Image);
        Assert.Null(Assert.Single(await store.AfterAsync(1)).Image);

        var full = Assert.IsType<ExcludeAnnouncement>(await store.GetFullAsync(1));
        Assert.Equal(new byte[] { 1, 2, 3 }, full.Image);
    }
}
