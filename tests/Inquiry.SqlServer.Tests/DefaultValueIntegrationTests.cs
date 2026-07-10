using System;
using System.Threading;
using System.Threading.Tasks;
using Inquiry.Entities;
using Inquiry.SqlServer.Tests.Fixtures;
using Inquiry.Stores;

namespace Inquiry.SqlServer.Tests;

[InquiryTable("TDefaultedItem")]
public sealed class DefaultedItem
{
    [InquiryKey]
    public Guid Key { get; set; } = Guid.NewGuid();

    [InquiryColumn]
    public string Name { get; set; } = string.Empty;

    [InquiryColumn(UseDatabaseDefault = true)]
    public string Status { get; set; } = string.Empty;
}

public partial class DefaultedItemStore : InquiryStore<DefaultedItem>
{
    [InquiryInsert(ReturnEntity = true)]
    public partial Task<DefaultedItem?> InsertReturningAsync(DefaultedItem item, CancellationToken cancellationToken = default);

    [InquiryUpdate(ReturnEntity = true)]
    public partial Task<DefaultedItem?> UpdateReturningAsync(DefaultedItem item, CancellationToken cancellationToken = default);

    [InquiryUpsert(ReturnEntity = true)]
    public partial Task<DefaultedItem?> UpsertReturningAsync(DefaultedItem item, CancellationToken cancellationToken = default);
}

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

[Collection(SqlServerCollection.Name)]
public sealed class DefaultValueIntegrationTests
{
    private readonly SqlServerContainerFixture _fixture;

    private const string DefaultedItemDdl =
        "CREATE TABLE [TDefaultedItem] ([Key] NVARCHAR(450) NOT NULL PRIMARY KEY, [Name] NVARCHAR(MAX) NOT NULL, [Status] NVARCHAR(MAX) DEFAULT 'New' NOT NULL);";

    private const string DefaultedKeyItemDdl =
        "CREATE TABLE [TDefaultedKeyItem] ([Id] NVARCHAR(450) DEFAULT LOWER(CONVERT(NVARCHAR(36), NEWID())) NOT NULL PRIMARY KEY, [Name] NVARCHAR(MAX) NOT NULL);";

    public DefaultValueIntegrationTests(SqlServerContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task InsertReturningUsesDatabaseDefaultForDefaultedColumn()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, DefaultedItemDdl, "defcol");
        var store = harness.GetRequiredService<DefaultedItemStore>();
        var item = new DefaultedItem { Name = "Widget" };

        var returned = await store.InsertReturningAsync(item);

        Assert.NotNull(returned);
        Assert.Equal(item.Key, returned.Key);
        Assert.Equal("Widget", returned.Name);
        Assert.Equal("New", returned.Status);
    }

    [SkippableFact]
    public async Task UpdateReturningCanSetDefaultedColumnAfterInsert()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, DefaultedItemDdl, "defcol");
        var store = harness.GetRequiredService<DefaultedItemStore>();
        var inserted = await store.InsertReturningAsync(new DefaultedItem { Name = "Widget" });

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
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, DefaultedItemDdl, "defcol");
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

    [SkippableFact]
    public async Task InsertReturningUsesDatabaseDefaultForPrimaryKey()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, DefaultedKeyItemDdl, "defkey");
        var store = harness.GetRequiredService<DefaultedKeyItemStore>();

        var returned = await store.InsertReturningAsync(new DefaultedKeyItem { Name = "Generated Key" });

        Assert.NotNull(returned);
        Assert.False(string.IsNullOrWhiteSpace(returned.Id));
        Assert.Equal("Generated Key", returned.Name);
    }

    [SkippableFact]
    public async Task UpsertReturningUsesDatabaseDefaultForNullPrimaryKey()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, DefaultedKeyItemDdl, "defkey");
        var store = harness.GetRequiredService<DefaultedKeyItemStore>();

        var returned = await store.UpsertReturningAsync(new DefaultedKeyItem { Name = "Generated Key" });

        Assert.NotNull(returned);
        Assert.False(string.IsNullOrWhiteSpace(returned.Id));
        Assert.Equal("Generated Key", returned.Name);
    }
}
