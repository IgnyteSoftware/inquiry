using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Inquiry.Entities;
using Inquiry.SqlServer.Tests.Fixtures;
using Inquiry.Stores;

namespace Inquiry.SqlServer.Tests;

[InquiryTable("SsGenItem")]
public sealed class SsGenItem
{
    [InquiryKey(IsGenerated = true)]
    public int? Id { get; set; }

    [InquiryColumn]
    public string Name { get; set; } = string.Empty;
}

public partial class SsGenItemStore : InquiryStore<SsGenItem>
{
    [InquiryUpsert]
    public partial Task<SsGenItem?> UpsertReturningAsync(SsGenItem item, CancellationToken cancellationToken = default);

    [InquirySelectAll]
    public partial Task<IReadOnlyList<SsGenItem>> SelectAllAsync(CancellationToken cancellationToken = default);
}

[Collection(SqlServerCollection.Name)]
public sealed class GeneratedKeyUpsertIntegrationTests
{
    private readonly SqlServerContainerFixture _fixture;

    private const string Ddl =
        "CREATE TABLE [SsGenItem] ([Id] INT IDENTITY(1,1) PRIMARY KEY, [Name] NVARCHAR(MAX) NOT NULL);";

    public GeneratedKeyUpsertIntegrationTests(SqlServerContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task UpsertReturningUsesGeneratedKeyWhenKeyIsNull()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "gkupsert");
        var store = harness.GetRequiredService<SsGenItemStore>();

        var returned = await store.UpsertReturningAsync(new SsGenItem { Name = "Generated" });

        Assert.NotNull(returned);
        Assert.NotNull(returned.Id);
        Assert.True(returned.Id > 0);
        Assert.Equal("Generated", returned.Name);
    }

    [SkippableFact]
    public async Task UpsertReturningInsertsExplicitMissingGeneratedKey()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "gkupsert");
        var store = harness.GetRequiredService<SsGenItemStore>();

        var returned = await store.UpsertReturningAsync(new SsGenItem { Id = 42, Name = "Explicit" });

        Assert.NotNull(returned);
        Assert.Equal(42, returned.Id);
        Assert.Equal("Explicit", returned.Name);
    }

    [SkippableFact]
    public async Task UpsertReturningUpdatesExistingGeneratedKey()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "gkupsert");
        var store = harness.GetRequiredService<SsGenItemStore>();

        await store.UpsertReturningAsync(new SsGenItem { Id = 7, Name = "Original" });
        var returned = await store.UpsertReturningAsync(new SsGenItem { Id = 7, Name = "Updated" });

        Assert.NotNull(returned);
        Assert.Equal(7, returned.Id);
        Assert.Equal("Updated", returned.Name);
    }

    [SkippableFact]
    public async Task ExplicitKeyUpsertInsertsThenUpdatesLeavingOneRow()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "gkupsert");
        var store = harness.GetRequiredService<SsGenItemStore>();

        await store.UpsertReturningAsync(new SsGenItem { Id = 5, Name = "A" });
        await store.UpsertReturningAsync(new SsGenItem { Id = 5, Name = "B" });

        var all = await store.SelectAllAsync();
        Assert.Single(all);
        Assert.Equal("B", all[0].Name);
    }
}
