using Inquiry.PostgreSql.Tests.Fixtures;
using Inquiry.Tests.UpsertMatchOn;

namespace Inquiry.PostgreSql.Tests;

/// <summary>#614: upserts matched on a unique natural key against a live PostgreSql engine.</summary>
[Collection(PostgreSqlCollection.Name)]
public sealed class UpsertMatchOnIntegrationTests
{
    private const string Ddl =
        "CREATE TABLE \"MatchPref\" (\"Id\" SERIAL PRIMARY KEY, \"MemberKey\" INTEGER NOT NULL, \"TypeCode\" VARCHAR(20) NOT NULL, \"Weight\" INTEGER NOT NULL, UNIQUE (\"MemberKey\", \"TypeCode\")); CREATE TABLE \"MatchTag\" (\"Id\" VARCHAR(36) PRIMARY KEY, \"Code\" VARCHAR(20) NOT NULL UNIQUE, \"Label\" VARCHAR(50) NOT NULL);";

    private readonly PostgreSqlContainerFixture _fixture;

    public UpsertMatchOnIntegrationTests(PostgreSqlContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task InsertThenUpdateKeepsGeneratedKey()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await PostgreSqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "matchon");
        await UpsertMatchOnScenarios.InsertThenUpdateKeepsGeneratedKeyAsync(harness.GetRequiredService<MatchPrefStore>());
    }

    [SkippableFact]
    public async Task NonReturningUpsertInsertsThenUpdates()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await PostgreSqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "matchon");
        await UpsertMatchOnScenarios.NonReturningUpsertInsertsThenUpdatesAsync(harness.GetRequiredService<MatchPrefStore>());
    }

    [SkippableFact]
    public async Task AssignedKeyOfExistingRowDoesNotChange()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await PostgreSqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "matchon");
        await UpsertMatchOnAssignedKeyScenarios.AssignedKeyOfExistingRowDoesNotChangeAsync(harness.GetRequiredService<MatchTagStore>());
    }

    [SkippableFact]
    public async Task ParallelUpsertsOfOneNaturalKeyAllSucceed()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await PostgreSqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "matchon");
        var failures = await UpsertMatchOnScenarios.ParallelUpsertsOfOneNaturalKeyAsync(harness.GetRequiredService<MatchPrefStore>());

        Assert.Empty(failures);
    }
}
