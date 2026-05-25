using Inquiry.Sqlite.Tests.Fixtures;

namespace Inquiry.Sqlite.Tests;

public sealed class DefaultValueIntegrationTests
{
    [Fact]
    public async Task InsertReturningUsesDatabaseDefaultForDefaultedColumn()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Schemas.DefaultedItem, "DefaultedColumn");
        var store = harness.GetRequiredService<DefaultedItemStore>();
        var item = new DefaultedItem { Name = "Widget" };

        var returned = await store.InsertReturningAsync(item);

        Assert.NotNull(returned);
        Assert.Equal(item.Key, returned.Key);
        Assert.Equal("Widget", returned.Name);
        Assert.Equal("New", returned.Status);
    }

    [Fact]
    public async Task UpdateReturningCanSetDefaultedColumnAfterInsert()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Schemas.DefaultedItem, "DefaultedColumn");
        var store = harness.GetRequiredService<DefaultedItemStore>();
        var inserted = await store.InsertReturningAsync(new DefaultedItem { Name = "Widget" });

        Assert.NotNull(inserted);
        inserted.Status = "Archived";
        var updated = await store.UpdateReturningAsync(inserted);

        Assert.NotNull(updated);
        Assert.Equal("Archived", updated.Status);
    }

    [Fact]
    public async Task UpsertReturningUsesDatabaseDefaultOnInsertAndParameterOnUpdate()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Schemas.DefaultedItem, "DefaultedColumn");
        var store = harness.GetRequiredService<DefaultedItemStore>();
        var item = new DefaultedItem { Name = "Widget", Status = "Ignored on insert" };

        var inserted = await store.UpsertReturningAsync(item);
        item.Status = "Changed";
        var updated = await store.UpsertReturningAsync(item);

        Assert.NotNull(inserted);
        Assert.Equal("New", inserted.Status);
        Assert.NotNull(updated);
        Assert.Equal("Changed", updated.Status);
    }

    [Fact]
    public async Task InsertReturningUsesDatabaseDefaultForPrimaryKey()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Schemas.DefaultedKeyItem, "DefaultedKey");
        var store = harness.GetRequiredService<DefaultedKeyItemStore>();

        var returned = await store.InsertReturningAsync(new DefaultedKeyItem { Name = "Generated Key" });

        Assert.NotNull(returned);
        Assert.False(string.IsNullOrWhiteSpace(returned.Id));
        Assert.Equal("Generated Key", returned.Name);
    }

    [Fact]
    public async Task UpsertReturningUsesDatabaseDefaultForNullPrimaryKey()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Schemas.DefaultedKeyItem, "DefaultedKey");
        var store = harness.GetRequiredService<DefaultedKeyItemStore>();

        var returned = await store.UpsertReturningAsync(new DefaultedKeyItem { Name = "Generated Key" });

        Assert.NotNull(returned);
        Assert.False(string.IsNullOrWhiteSpace(returned.Id));
        Assert.Equal("Generated Key", returned.Name);
    }
}
