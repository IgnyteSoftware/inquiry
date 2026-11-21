using Inquiry.Entities;
using Inquiry.Sqlite.Tests.Fixtures;
using Inquiry.Stores;

namespace Inquiry.Sqlite.Tests;

[InquiryTable("Setting", GenerateDdl = false)]
public sealed class SelectOneSetting
{
    [InquiryKey(IsGenerated = true)]
    public long Id { get; set; }

    [InquiryColumn("Name")]
    public string Name { get; set; } = string.Empty;

    [InquiryColumn("IsDeleted"), InquirySoftDelete]
    public bool IsDeleted { get; set; }
}

public partial class SelectOneSettingStore : InquiryStore<SelectOneSetting>
{
    [InquiryInsert]
    public partial Task<int> InsertAsync(SelectOneSetting setting, CancellationToken cancellationToken = default);

    [InquirySelectOneByField(nameof(SelectOneSetting.Name))]
    public partial Task<SelectOneSetting?> GetByNameAsync(string name, CancellationToken cancellationToken = default);

    [InquirySelectOneByField(nameof(SelectOneSetting.Name), IncludeDeleted = true)]
    public partial Task<SelectOneSetting?> GetAnyByNameAsync(string name, CancellationToken cancellationToken = default);
}

/// <summary>
/// <c>[InquirySelectOneByField]</c> end-to-end against SQLite (#609): the row or null, a second match
/// throws, and soft delete applies unless <c>IncludeDeleted</c> is set.
/// </summary>
public sealed class SelectOneByFieldIntegrationTests
{
    private const string Ddl = "CREATE TABLE Setting (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL, IsDeleted INTEGER NOT NULL);";

    [Fact]
    public async Task ReturnsRowOrNullAndRejectsSecondMatch()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Ddl, "SelectOneByField");
        var store = harness.GetRequiredService<SelectOneSettingStore>();

        await store.InsertAsync(new SelectOneSetting { Name = "theme" });
        await store.InsertAsync(new SelectOneSetting { Name = "locale" });
        await store.InsertAsync(new SelectOneSetting { Name = "locale" });

        Assert.Equal("theme", (await store.GetByNameAsync("theme"))?.Name);
        Assert.Null(await store.GetByNameAsync("missing"));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => store.GetByNameAsync("locale"));
        Assert.Equal(
            "Store method 'SelectOneSettingStore.GetByNameAsync' expects at most one row with a matching 'Name', but more than one row matched.",
            error.Message);
    }

    [Fact]
    public async Task SoftDeletedRowsCountOnlyWithIncludeDeleted()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Ddl, "SelectOneByFieldSoftDelete");
        var store = harness.GetRequiredService<SelectOneSettingStore>();

        await store.InsertAsync(new SelectOneSetting { Name = "theme" });
        await store.InsertAsync(new SelectOneSetting { Name = "theme", IsDeleted = true });
        await store.InsertAsync(new SelectOneSetting { Name = "retired", IsDeleted = true });

        // The deleted duplicate is filtered out, so the active row is the only match.
        Assert.False((await store.GetByNameAsync("theme"))?.IsDeleted);
        Assert.Null(await store.GetByNameAsync("retired"));

        Assert.True((await store.GetAnyByNameAsync("retired"))?.IsDeleted);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.GetAnyByNameAsync("theme"));
    }
}
