using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Inquiry.Entities;
using Inquiry.PostgreSql.Tests.Fixtures;
using Inquiry.Stores;

namespace Inquiry.PostgreSql.Tests;

// #47: a key-only generated entity has both an empty upsert SET clause and an empty INSERT column list,
// which broke PostgreSQL's generated-key upsert (`() SELECT` and empty `DO UPDATE SET `).
[InquiryTable("PgKeyOnlyItem")]
public sealed class PgKeyOnlyItem
{
    [InquiryKey(IsGenerated = true)]
    public long? Id { get; set; }
}

public partial class PgKeyOnlyItemStore : InquiryStore<PgKeyOnlyItem>
{
    [InquiryUpsert]
    public partial Task<int> UpsertAsync(PgKeyOnlyItem item, CancellationToken ct = default);

    [InquiryUpsert(ReturnEntity = true)]
    public partial Task<PgKeyOnlyItem?> UpsertReturningAsync(PgKeyOnlyItem item, CancellationToken ct = default);

    [InquirySelectAll]
    public partial Task<IReadOnlyList<PgKeyOnlyItem>> AllAsync(CancellationToken ct = default);
}

/// <summary>#47 against real PostgreSQL: a generated-key upsert on a key-only entity must emit valid SQL
/// (no empty <c>() SELECT</c> / empty <c>DO UPDATE SET </c>); the conflict branch is a DO NOTHING no-op.</summary>
[Collection(PostgreSqlCollection.Name)]
public sealed class EmptyUpsertSetIntegrationTests
{
    private readonly PostgreSqlContainerFixture _fixture;
    public EmptyUpsertSetIntegrationTests(PostgreSqlContainerFixture fixture) => _fixture = fixture;

    private const string Ddl = "CREATE TABLE \"PgKeyOnlyItem\" (\"Id\" BIGSERIAL PRIMARY KEY);";

    [SkippableFact]
    public async Task KeyOnlyEntityUpsertEmitsValidSqlAndConflictIsNoOp()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await PostgreSqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "keyonly");
        var store = harness.GetRequiredService<PgKeyOnlyItemStore>();

        // Null key → BIGSERIAL generates it (routed to the plain insert).
        await store.UpsertAsync(new PgKeyOnlyItem { Id = null });
        var id = Assert.Single(await store.AllAsync()).Id;
        Assert.NotNull(id);

        // Explicit existing key → conflict → DO NOTHING. Before the fix the generated-key upsert SQL was
        // syntactically invalid and failed to parse on PostgreSQL.
        await store.UpsertAsync(new PgKeyOnlyItem { Id = id });
        Assert.Single(await store.AllAsync());

        // A returning upsert that conflicts on an existing key returns null: the DO NOTHING branch produces
        // no row. This is the accepted consequence of a no-op conflict for a key-only entity (there is
        // nothing to update or return), not an error.
        var conflict = await store.UpsertReturningAsync(new PgKeyOnlyItem { Id = id });
        Assert.Null(conflict);
        Assert.Single(await store.AllAsync());

        // A returning upsert with a null key takes the generate path and returns the freshly inserted row.
        var inserted = await store.UpsertReturningAsync(new PgKeyOnlyItem { Id = null });
        Assert.NotNull(inserted);
        Assert.NotNull(inserted!.Id);
        Assert.Equal(2, (await store.AllAsync()).Count);
    }
}
