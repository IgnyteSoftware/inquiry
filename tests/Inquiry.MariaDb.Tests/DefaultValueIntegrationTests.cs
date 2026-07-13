using System;
using System.Threading;
using System.Threading.Tasks;
using Inquiry.Entities;
using Inquiry.MariaDb.Tests.Fixtures;
using Inquiry.Stores;

namespace Inquiry.MariaDb.Tests;

[InquiryTable("TDefaultedKeyItem")]
public sealed class DefaultedKeyItem
{
    [InquiryKey(UseDatabaseDefault = true, Length = 255)]
    public string? Id { get; set; }

    [InquiryColumn]
    public string Name { get; set; } = string.Empty;
}

public partial class DefaultedKeyItemStore : InquiryStore<DefaultedKeyItem>
{
    [InquiryInsert(ReturnEntity = true)]
    public partial Task<DefaultedKeyItem?> InsertReturningAsync(DefaultedKeyItem item, CancellationToken cancellationToken = default);

    [InquiryUpsert(ReturnEntity = true)]
    public partial Task<DefaultedKeyItem?> UpsertReturningAsync(DefaultedKeyItem item, CancellationToken cancellationToken = default);
}

[InquiryTable("TDefaultedUniqueKeyItem")]
public sealed class DefaultedUniqueKeyItem
{
    [InquiryKey(UseDatabaseDefault = true, Length = 255)]
    public string? Id { get; set; }

    [InquiryColumn(IsUnique = true, Length = 255)]
    public string Code { get; set; } = string.Empty;

    [InquiryColumn]
    public string Name { get; set; } = string.Empty;
}

public partial class DefaultedUniqueKeyItemStore : InquiryStore<DefaultedUniqueKeyItem>
{
    [InquiryUpsert(ReturnEntity = true)]
    public partial Task<DefaultedUniqueKeyItem?> UpsertReturningAsync(DefaultedUniqueKeyItem item, CancellationToken cancellationToken = default);
}

[Collection(MariaDbCollection.Name)]
public sealed class DefaultValueIntegrationTests
{
    private readonly MariaDbContainerFixture _fixture;

    private const string DefaultedItemDdl =
        "CREATE TABLE `TDefaultedItem` (`Key` BIGINT NOT NULL PRIMARY KEY, `Name` VARCHAR(255) NOT NULL, `Status` VARCHAR(255) DEFAULT 'New' NOT NULL);";

    private const string DefaultedKeyItemDdl =
        "CREATE TABLE `TDefaultedKeyItem` (`Id` VARCHAR(255) DEFAULT (UUID()) NOT NULL PRIMARY KEY, `Name` VARCHAR(255) NOT NULL);";

    private const string DefaultedUniqueKeyItemDdl =
        "CREATE TABLE `TDefaultedUniqueKeyItem` (`Id` VARCHAR(255) DEFAULT (UUID()) NOT NULL PRIMARY KEY, `Code` VARCHAR(255) NOT NULL UNIQUE, `Name` VARCHAR(255) NOT NULL);";

    public DefaultValueIntegrationTests(MariaDbContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task InsertReturningUsesDatabaseDefaultForDefaultedColumn()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MariaDbTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, DefaultedItemDdl, "defcol");
        var store = harness.GetRequiredService<DefaultedItemStore>();
        var item = new DefaultedItem { Key = 1, Name = "Widget" };

        var returned = await store.InsertReturningAsync(item);

        Assert.NotNull(returned);
        Assert.Equal(1L, returned.Key);
        Assert.Equal("Widget", returned.Name);
        Assert.Equal("New", returned.Status);
    }

    [SkippableFact]
    public async Task UpdateReturningCanSetDefaultedColumnAfterInsert()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MariaDbTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, DefaultedItemDdl, "defcol");
        var store = harness.GetRequiredService<DefaultedItemStore>();
        var inserted = await store.InsertReturningAsync(new DefaultedItem { Key = 1, Name = "Widget" });

        Assert.NotNull(inserted);
        inserted.Status = "Archived";
        var updated = await store.UpdateReturningAsync(inserted);

        Assert.NotNull(updated);
        Assert.Equal("Archived", updated.Status);
    }

    [SkippableFact]
    public async Task UpsertReturningUsesDatabaseDefaultOnInsertAndParameterOnUpdate()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MariaDbTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, DefaultedItemDdl, "defcol");
        var store = harness.GetRequiredService<DefaultedItemStore>();
        var item = new DefaultedItem { Key = 1, Name = "Widget", Status = "Ignored on insert" };

        var inserted = await store.UpsertReturningAsync(item);
        item.Status = "Changed";
        var updated = await store.UpsertReturningAsync(item);

        Assert.NotNull(inserted);
        Assert.Equal("New", inserted.Status);
        Assert.NotNull(updated);
        Assert.Equal("Changed", updated.Status);
    }

    [SkippableFact]
    public async Task InsertReturningUsesDatabaseDefaultForPrimaryKey()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MariaDbTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, DefaultedKeyItemDdl, "defkey");
        var store = harness.GetRequiredService<DefaultedKeyItemStore>();

        var returned = await store.InsertReturningAsync(new DefaultedKeyItem { Name = "Generated Key" });

        Assert.NotNull(returned);
        Assert.False(string.IsNullOrWhiteSpace(returned.Id));
        Assert.Equal("Generated Key", returned.Name);
    }

    [SkippableFact]
    public async Task InsertReturningUsesDatabaseDefaultForEachCallAndIgnoresEntityKey()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MariaDbTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, DefaultedKeyItemDdl, "defkeyrepeat");
        var store = harness.GetRequiredService<DefaultedKeyItemStore>();

        var first = await store.InsertReturningAsync(new DefaultedKeyItem { Id = "caller-value", Name = "First" });
        var second = await store.InsertReturningAsync(new DefaultedKeyItem { Id = "another-caller-value", Name = "Second" });

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotEqual("caller-value", first.Id);
        Assert.NotEqual("another-caller-value", second.Id);
        Assert.NotEqual(first.Id, second.Id);
    }

    [SkippableFact]
    public async Task UpsertReturningUsesDatabaseDefaultForNullPrimaryKey()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MariaDbTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, DefaultedKeyItemDdl, "defkey");
        var store = harness.GetRequiredService<DefaultedKeyItemStore>();

        var returned = await store.UpsertReturningAsync(new DefaultedKeyItem { Name = "Generated Key" });

        Assert.NotNull(returned);
        Assert.False(string.IsNullOrWhiteSpace(returned.Id));
        Assert.Equal("Generated Key", returned.Name);
    }

    [SkippableFact]
    public async Task UpsertReturningWithExplicitDefaultKeyHandlesInsertAndPrimaryKeyConflict()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MariaDbTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, DefaultedKeyItemDdl, "defkeyexplicit");
        var store = harness.GetRequiredService<DefaultedKeyItemStore>();

        var inserted = await store.UpsertReturningAsync(new DefaultedKeyItem { Id = "explicit-key", Name = "Inserted" });
        var updated = await store.UpsertReturningAsync(new DefaultedKeyItem { Id = "explicit-key", Name = "Updated" });

        Assert.NotNull(inserted);
        Assert.Equal("explicit-key", inserted.Id);
        Assert.NotNull(updated);
        Assert.Equal("explicit-key", updated.Id);
        Assert.Equal("Updated", updated.Name);
    }

    [SkippableFact]
    public async Task NativeReturningReportsRowThatWinsSecondaryUniqueConflict()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MariaDbTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, DefaultedUniqueKeyItemDdl, "defkeyunique");
        var store = harness.GetRequiredService<DefaultedUniqueKeyItemStore>();

        var inserted = await store.UpsertReturningAsync(new DefaultedUniqueKeyItem { Id = "original-key", Code = "same-code", Name = "Original" });
        var updated = await store.UpsertReturningAsync(new DefaultedUniqueKeyItem { Id = "losing-key", Code = "same-code", Name = "Updated" });

        Assert.NotNull(inserted);
        Assert.NotNull(updated);
        Assert.Equal("original-key", updated.Id);
        Assert.Equal("Updated", updated.Name);
    }
}
