using Inquiry.MySql.Tests.Fixtures;
using Inquiry.Tests.UpsertMatchOn;

namespace Inquiry.MySql.Tests;

// INQ121 rejects a MatchOn upsert whose key is not AUTO_INCREMENT on this provider.
/// <summary>#614: upserts matched on a unique natural key against a live MySql engine.</summary>
[Collection(MySqlCollection.Name)]
public sealed class UpsertMatchOnIntegrationTests
{
    private const string Ddl =
        "CREATE TABLE MatchPref (Id INT NOT NULL AUTO_INCREMENT PRIMARY KEY, MemberKey INT NOT NULL, TypeCode VARCHAR(20) NOT NULL, Weight INT NOT NULL, UNIQUE KEY UX_MatchPref (MemberKey, TypeCode));";

    private readonly MySqlContainerFixture _fixture;

    public UpsertMatchOnIntegrationTests(MySqlContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task InsertThenUpdateKeepsGeneratedKey()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "matchon");
        await UpsertMatchOnScenarios.InsertThenUpdateKeepsGeneratedKeyAsync(harness.GetRequiredService<MatchPrefStore>());
    }

    [SkippableFact]
    public async Task NonReturningUpsertInsertsThenUpdates()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "matchon");
        await UpsertMatchOnScenarios.NonReturningUpsertInsertsThenUpdatesAsync(harness.GetRequiredService<MatchPrefStore>());
    }

    [SkippableFact]
    public async Task ParallelUpsertsOfOneNaturalKeyAllSucceed()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "matchon");
        var failures = await UpsertMatchOnScenarios.ParallelUpsertsOfOneNaturalKeyAsync(harness.GetRequiredService<MatchPrefStore>());

        Assert.Empty(failures);
    }
}
