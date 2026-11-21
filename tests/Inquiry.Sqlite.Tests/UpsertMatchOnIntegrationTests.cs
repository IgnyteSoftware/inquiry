using Inquiry.Sqlite.Tests.Fixtures;
using Inquiry.Tests.UpsertMatchOn;

namespace Inquiry.Sqlite.Tests;

/// <summary>#614: upserts matched on a unique natural key against SQLite.</summary>
public sealed class UpsertMatchOnIntegrationTests
{
    private const string Ddl =
        "CREATE TABLE \"MatchPref\" (\"Id\" INTEGER PRIMARY KEY AUTOINCREMENT, \"MemberKey\" INTEGER NOT NULL, \"TypeCode\" TEXT NOT NULL, \"Weight\" INTEGER NOT NULL, UNIQUE (\"MemberKey\", \"TypeCode\")); CREATE TABLE \"MatchTag\" (\"Id\" TEXT PRIMARY KEY, \"Code\" TEXT NOT NULL UNIQUE, \"Label\" TEXT NOT NULL);";

    [Fact]
    public async Task InsertThenUpdateKeepsGeneratedKey()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Ddl, "MatchOn");
        await UpsertMatchOnScenarios.InsertThenUpdateKeepsGeneratedKeyAsync(harness.GetRequiredService<MatchPrefStore>());
    }

    [Fact]
    public async Task NonReturningUpsertInsertsThenUpdates()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Ddl, "MatchOn");
        await UpsertMatchOnScenarios.NonReturningUpsertInsertsThenUpdatesAsync(harness.GetRequiredService<MatchPrefStore>());
    }

    [Fact]
    public async Task AssignedKeyOfExistingRowDoesNotChange()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Ddl, "MatchOn");
        await UpsertMatchOnAssignedKeyScenarios.AssignedKeyOfExistingRowDoesNotChangeAsync(harness.GetRequiredService<MatchTagStore>());
    }

    [Fact]
    public async Task ParallelUpsertsOfOneNaturalKeyAllSucceed()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Ddl, "MatchOn");
        var failures = await UpsertMatchOnScenarios.ParallelUpsertsOfOneNaturalKeyAsync(harness.GetRequiredService<MatchPrefStore>());

        Assert.Empty(failures);
    }
}
