using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Inquiry.Entities;
using Inquiry.Sqlite.Tests.Fixtures;
using Inquiry.Stores;

namespace Inquiry.Sqlite.Tests;

// #47: an entity whose only column is a database-generated key has an empty upsert SET clause.
[InquiryTable("KeyOnlyItem")]
public sealed class KeyOnlyItem
{
    [InquiryKey(IsGenerated = true)]
    public long? Id { get; set; }
}

public partial class KeyOnlyItemStore : InquiryStore<KeyOnlyItem>
{
    [InquiryUpsert]
    public partial Task<int> UpsertAsync(KeyOnlyItem item, CancellationToken ct = default);

    [InquiryUpsert(ReturnEntity = true)]
    public partial Task<KeyOnlyItem?> UpsertReturningAsync(KeyOnlyItem item, CancellationToken ct = default);

    [InquirySelectAll]
    public partial Task<IReadOnlyList<KeyOnlyItem>> AllAsync(CancellationToken ct = default);
}

/// <summary>#47: a generated-key upsert on a key-only entity must emit valid SQL — the conflict branch
/// is a no-op (DO NOTHING). Before the fix this emitted an empty <c>DO UPDATE SET </c> and failed.</summary>
public sealed class EmptyUpsertSetIntegrationTests
{
    private const string Ddl = "CREATE TABLE KeyOnlyItem (Id INTEGER PRIMARY KEY AUTOINCREMENT);";

    [Fact]
    public async Task KeyOnlyEntityUpsertInsertsThenConflictIsNoOp()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Ddl, "keyonly");
        var store = harness.GetRequiredService<KeyOnlyItemStore>();

        // Null key → database generates it (routed to the plain insert).
        await store.UpsertAsync(new KeyOnlyItem { Id = null });
        var id = Assert.Single(await store.AllAsync()).Id;
        Assert.NotNull(id);

        // Upsert the existing key → conflict → DO NOTHING (a valid no-op, no SQL error, no extra row).
        await store.UpsertAsync(new KeyOnlyItem { Id = id });
        Assert.Single(await store.AllAsync());

        // A returning upsert that conflicts returns null: DO NOTHING produces no row to return. This is the
        // accepted consequence of a no-op conflict for a key-only entity, not an error.
        var conflict = await store.UpsertReturningAsync(new KeyOnlyItem { Id = id });
        Assert.Null(conflict);
        Assert.Single(await store.AllAsync());

        // A returning upsert with a null key takes the generate path and returns the freshly inserted row.
        var inserted = await store.UpsertReturningAsync(new KeyOnlyItem { Id = null });
        Assert.NotNull(inserted);
        Assert.NotNull(inserted!.Id);
        Assert.Equal(2, (await store.AllAsync()).Count);
    }
}
