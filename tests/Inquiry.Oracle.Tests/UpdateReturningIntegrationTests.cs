using Inquiry.Oracle.Tests.Fixtures;
using Inquiry.Tests.UpdateReturning;

namespace Inquiry.Oracle.Tests;

/// <summary>
/// Set-based updates that return changed values. Oracle collects the updated ROWIDs in a PL/SQL block
/// and opens a cursor over those rows while the UPDATE still holds their locks.
/// </summary>
[Collection(OracleCollection.Name)]
public sealed class UpdateReturningIntegrationTests
{
    private const string Ddl = "CREATE TABLE ReturningCounter (Id NUMBER(10) PRIMARY KEY, Category VARCHAR2(50) NOT NULL, Hits NUMBER(10) NOT NULL)";

    private readonly OracleContainerFixture _fixture;
    public UpdateReturningIntegrationTests(OracleContainerFixture fixture) => _fixture = fixture;

    private Task<OracleTestHarness> CreateAsync()
        => OracleTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "updreturning");

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
