using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Inquiry.Entities;
using Inquiry.MariaDb.Tests.Fixtures;
using Inquiry.Stores;

namespace Inquiry.MariaDb.Tests;

[InquiryTable("MariaDbKeyOnlyItem")]
public sealed class MariaDbKeyOnlyItem
{
    [InquiryKey(IsGenerated = true)]
    public long? Id { get; set; }
}

public partial class MariaDbKeyOnlyItemStore : InquiryStore<MariaDbKeyOnlyItem>
{
    [InquiryUpsert]
    public partial Task<int> UpsertAsync(MariaDbKeyOnlyItem item, CancellationToken ct = default);

    [InquiryUpsert]
    public partial Task<MariaDbKeyOnlyItem?> UpsertReturningAsync(MariaDbKeyOnlyItem item, CancellationToken ct = default);

    [InquirySelectAll]
    public partial Task<IReadOnlyList<MariaDbKeyOnlyItem>> AllAsync(CancellationToken ct = default);
}

/// <summary>#157 against real MariaDB: a generated-key upsert on a key-only entity must emit valid SQL.
/// Unlike PostgreSQL/SQLite (which use DO NOTHING and return null on conflict), MariaDB uses
/// ON DUPLICATE KEY UPDATE key = key (a no-op match), so the returning upsert returns the
/// matched row rather than null.</summary>
[Collection(MariaDbCollection.Name)]
public sealed class EmptyUpsertSetIntegrationTests
{
    private readonly MariaDbContainerFixture _fixture;
    public EmptyUpsertSetIntegrationTests(MariaDbContainerFixture fixture) => _fixture = fixture;

    private const string Ddl = "CREATE TABLE `MariaDbKeyOnlyItem` (`Id` BIGINT AUTO_INCREMENT PRIMARY KEY);";

    [SkippableFact]
    public async Task KeyOnlyEntityUpsertEmitsValidSqlAndConflictReturnsMatchedRow()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MariaDbTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString, Ddl, "keyonly");
        var store = harness.GetRequiredService<MariaDbKeyOnlyItemStore>();

        // Null key → AUTO_INCREMENT generates it.
        await store.UpsertAsync(new MariaDbKeyOnlyItem { Id = null });
        var id = Assert.Single(await store.AllAsync()).Id;
        Assert.NotNull(id);

        // Explicit existing key → conflict → ON DUPLICATE KEY UPDATE key = key (no-op).
        await store.UpsertAsync(new MariaDbKeyOnlyItem { Id = id });
        Assert.Single(await store.AllAsync());

        // MariaDB cannot DO NOTHING; the no-op UPDATE still "matches" the row, so the trailing
        // SELECT finds and returns it — unlike PostgreSQL/SQLite which return null.
        var conflict = await store.UpsertReturningAsync(new MariaDbKeyOnlyItem { Id = id });
        Assert.NotNull(conflict);
        Assert.Equal(id, conflict!.Id);
        Assert.Single(await store.AllAsync());

        // A returning upsert with a null key takes the generate path and returns the inserted row.
        var inserted = await store.UpsertReturningAsync(new MariaDbKeyOnlyItem { Id = null });
        Assert.NotNull(inserted);
        Assert.NotNull(inserted!.Id);
        Assert.Equal(2, (await store.AllAsync()).Count);
    }
}
