using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Inquiry.Entities;
using Inquiry.PostgreSql.Tests.Fixtures;
using Inquiry.Stores;

namespace Inquiry.PostgreSql.Tests;

[InquiryTable("PgGenItem")]
public sealed class PgGenItem
{
    [InquiryKey(IsGenerated = true)]
    public long? Id { get; set; }

    [InquiryColumn]
    public string Name { get; set; } = string.Empty;
}

public partial class PgGenItemStore : InquiryStore<PgGenItem>
{
    [InquiryUpsert(ReturnEntity = true)]
    public partial Task<PgGenItem?> UpsertReturningAsync(PgGenItem item, CancellationToken cancellationToken = default);

    [InquirySelectAll]
    public partial Task<IReadOnlyList<PgGenItem>> SelectAllAsync(CancellationToken cancellationToken = default);
}

[Collection(PostgreSqlCollection.Name)]
public sealed class GeneratedKeyUpsertIntegrationTests
{
    private readonly PostgreSqlContainerFixture _fixture;

    private const string Ddl =
        """CREATE TABLE "PgGenItem" ("Id" BIGSERIAL PRIMARY KEY, "Name" TEXT NOT NULL);""";

    public GeneratedKeyUpsertIntegrationTests(PostgreSqlContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task UpsertReturningUsesGeneratedKeyWhenKeyIsNull()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await PostgreSqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "gkupsert");
        var store = harness.GetRequiredService<PgGenItemStore>();

        var returned = await store.UpsertReturningAsync(new PgGenItem { Name = "Generated" });

        Assert.NotNull(returned);
        Assert.NotNull(returned.Id);
        Assert.True(returned.Id > 0);
        Assert.Equal("Generated", returned.Name);
    }

    [SkippableFact]
    public async Task UpsertReturningInsertsExplicitMissingGeneratedKey()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await PostgreSqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "gkupsert");
        var store = harness.GetRequiredService<PgGenItemStore>();

        var returned = await store.UpsertReturningAsync(new PgGenItem { Id = 42, Name = "Explicit" });

        Assert.NotNull(returned);
        Assert.Equal(42L, returned.Id);
        Assert.Equal("Explicit", returned.Name);
    }

    [SkippableFact]
    public async Task UpsertReturningUpdatesExistingGeneratedKey()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await PostgreSqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "gkupsert");
        var store = harness.GetRequiredService<PgGenItemStore>();

        await store.UpsertReturningAsync(new PgGenItem { Id = 7, Name = "Original" });
        var returned = await store.UpsertReturningAsync(new PgGenItem { Id = 7, Name = "Updated" });

        Assert.NotNull(returned);
        Assert.Equal(7L, returned.Id);
        Assert.Equal("Updated", returned.Name);
    }

    [SkippableFact]
    public async Task ExplicitKeyUpsertInsertsThenUpdatesLeavingOneRow()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await PostgreSqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "gkupsert");
        var store = harness.GetRequiredService<PgGenItemStore>();

        await store.UpsertReturningAsync(new PgGenItem { Id = 5, Name = "A" });
        await store.UpsertReturningAsync(new PgGenItem { Id = 5, Name = "B" });

        var all = await store.SelectAllAsync();
        Assert.Single(all);
        Assert.Equal("B", all[0].Name);
    }
}
