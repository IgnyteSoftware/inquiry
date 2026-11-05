using Inquiry.FeatureCatalog.LockingRegression;
using Inquiry.PostgreSql.Tests.Fixtures;
using Inquiry.Tests.Shared;

namespace Inquiry.PostgreSql.Tests;

/// <summary>
/// Live row-locking regressions on PostgreSQL: a locking projection read holds the row (#539), and the lock shapes the generator accepts run (#538). DISTINCT with a lock is rejected at build time (INQ039), so it has no live check here.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
public sealed class RowLockRegressionIntegrationTests
{
    private readonly PostgreSqlContainerFixture _fixture;
    public RowLockRegressionIntegrationTests(PostgreSqlContainerFixture fixture) => _fixture = fixture;

    [SkippableTheory]
    [InlineData(false, true)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public async Task ProjectionReadHoldsTheRowOnlyWhenLocked(bool byField, bool locked)
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await CreateHarnessAsync();
        await RowLockRegressionScenarios.ProjectionReadHoldsTheRowOnlyWhenLockedAsync(harness.Services, byField, locked);
    }

    [SkippableFact]
    public async Task PagedLockModesRun()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await CreateHarnessAsync();
        await RowLockRegressionPagedScenarios.PagedLocksRunAsync(harness.Services);
    }

    private Task<PostgreSqlTestHarness> CreateHarnessAsync()
        => PostgreSqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, LockProbeSchema.PostgreSqlDdl, "lockreg", configureServices: LockRegressionCommandCounter.Register);
}
