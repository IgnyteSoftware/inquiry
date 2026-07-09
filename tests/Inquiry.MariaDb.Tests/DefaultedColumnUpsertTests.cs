using Inquiry.MariaDb.Tests.Fixtures;

namespace Inquiry.MariaDb.Tests;

/// <summary>
/// Pins the cross-dialect contract for <c>[InquiryColumn(UseDatabaseDefault = true)]</c>
/// during upsert against live MariaDB:
///   - INSERT branch: column omitted from VALUES, database default applies.
///   - UPDATE branch: column updated to the entity's intended value (NOT reset to default).
///
/// The SQLite test project (DefaultValueIntegrationTests) pins the same contract for the
/// SQLite dialect. This test exists because MariaDB's <c>ON DUPLICATE KEY UPDATE col = VALUES(col)</c>
/// pattern previously assigned every non-key, non-generated column from <c>VALUES(col)</c> —
/// which for a <c>UseDatabaseDefault</c> column resolves to the database default (the column
/// isn't in the attempted INSERT list), silently reverting the entity's update value.
/// </summary>
[Collection(MariaDbCollection.Name)]
public sealed class DefaultedColumnUpsertTests
{
    private readonly MariaDbContainerFixture _fixture;
    public DefaultedColumnUpsertTests(MariaDbContainerFixture fixture) => _fixture = fixture;

    private const string Ddl = """
        CREATE TABLE TDefaultedItem (
            `Key` BIGINT NOT NULL PRIMARY KEY,
            Name VARCHAR(100) NOT NULL,
            Status VARCHAR(50) NOT NULL DEFAULT 'New'
        );
        """;

    [SkippableFact]
    public async Task UpsertUpdateBranchPersistsEntityStatusInsteadOfReverttingToDatabaseDefault()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MariaDbTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString, Ddl, "defaulted_upsert");
        var store = harness.GetRequiredService<DefaultedItemStore>();

        // First upsert hits the INSERT branch (no existing row with key=1). Status is omitted
        // from the VALUES list since UseDatabaseDefault = true → DB default 'New' applied.
        var inserted = await store.UpsertReturningAsync(new DefaultedItem { Key = 1, Name = "Widget" });
        Assert.NotNull(inserted);
        Assert.Equal("New", inserted!.Status);

        // Second upsert with the same key hits the UPDATE branch (ON DUPLICATE KEY UPDATE).
        // The entity supplies Status = "Closed" — that's what the row should end up with.
        // Under the pre-fix MariaDB builder, this branch assigned `Status = VALUES(Status)`
        // which resolved to the default ('New') because the column was omitted from the
        // attempted insert → the row's Status silently reverted to 'New'.
        var updated = await store.UpsertReturningAsync(new DefaultedItem { Key = 1, Name = "Widget", Status = "Closed" });
        Assert.NotNull(updated);
        Assert.Equal("Closed", updated!.Status);

        // Final read-back from the row (not the returning select) confirms the persisted state.
        var read = await store.SelectByKeyAsync(1);
        Assert.NotNull(read);
        Assert.Equal("Closed", read!.Status);
    }
}
