using Inquiry.SqlServer.Tests.Fixtures;
using Inquiry.Tests.UpsertMatchOn;

namespace Inquiry.SqlServer.Tests;

/// <summary>#614: upserts matched on a unique natural key against a live SqlServer engine.</summary>
[Collection(SqlServerCollection.Name)]
public sealed class UpsertMatchOnIntegrationTests
{
    private const string Ddl =
        "CREATE TABLE [MatchPref] ([Id] INT IDENTITY(1,1) PRIMARY KEY, [MemberKey] INT NOT NULL, [TypeCode] NVARCHAR(20) NOT NULL, [Weight] INT NOT NULL, CONSTRAINT [UX_MatchPref] UNIQUE ([MemberKey], [TypeCode])); CREATE TABLE [MatchTag] ([Id] NVARCHAR(36) PRIMARY KEY, [Code] NVARCHAR(20) NOT NULL UNIQUE, [Label] NVARCHAR(50) NOT NULL);";

    private readonly SqlServerContainerFixture _fixture;

    public UpsertMatchOnIntegrationTests(SqlServerContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task InsertThenUpdateKeepsGeneratedKey()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "matchon");
        await UpsertMatchOnScenarios.InsertThenUpdateKeepsGeneratedKeyAsync(harness.GetRequiredService<MatchPrefStore>());
    }

    [SkippableFact]
    public async Task NonReturningUpsertInsertsThenUpdates()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "matchon");
        await UpsertMatchOnScenarios.NonReturningUpsertInsertsThenUpdatesAsync(harness.GetRequiredService<MatchPrefStore>());
    }

    [SkippableFact]
    public async Task AssignedKeyOfExistingRowDoesNotChange()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "matchon");
        await UpsertMatchOnAssignedKeyScenarios.AssignedKeyOfExistingRowDoesNotChangeAsync(harness.GetRequiredService<MatchTagStore>());
    }

    [SkippableFact]
    public async Task ParallelUpsertsOfOneNaturalKeyAllSucceed()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "matchon");
        var failures = await UpsertMatchOnScenarios.ParallelUpsertsOfOneNaturalKeyAsync(harness.GetRequiredService<MatchPrefStore>());

        Assert.Empty(failures);
    }
}
