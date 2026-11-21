using Inquiry.PostgreSql.Tests.Fixtures;
using Inquiry.Tests.UpdateReturning;

namespace Inquiry.PostgreSql.Tests;

/// <summary>Set-based updates that return changed values, read with PostgreSQL's UPDATE ... RETURNING.</summary>
[Collection(PostgreSqlCollection.Name)]
public sealed class UpdateReturningIntegrationTests
{
    private const string Ddl = @"CREATE TABLE ""ReturningCounter"" (""Id"" INT PRIMARY KEY, ""Category"" VARCHAR(50) NOT NULL, ""Hits"" INT NOT NULL);";

    private readonly PostgreSqlContainerFixture _fixture;
    public UpdateReturningIntegrationTests(PostgreSqlContainerFixture fixture) => _fixture = fixture;

    private Task<PostgreSqlTestHarness> CreateAsync()
        => PostgreSqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "updreturning");

    [SkippableFact]
    public async Task SingleValueReturnsTheNewValueOrNull()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await CreateAsync();
        await UpdateReturningScenarios.AssertSingleValueReturnsTheNewValueOrNullAsync(harness.GetRequiredService<ReturningCounterStore>());
    }

    [SkippableFact]
    public async Task ListReturnsOneEntryPerUpdatedRow()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await CreateAsync();
        await UpdateReturningScenarios.AssertListReturnsOneEntryPerUpdatedRowAsync(harness.GetRequiredService<ReturningCounterStore>());
    }

    [SkippableFact]
    public async Task RowsShowAChangedFilterColumn()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await CreateAsync();
        await UpdateReturningScenarios.AssertRowsShowAChangedFilterColumnAsync(harness.GetRequiredService<ReturningCounterStore>());
    }
}
