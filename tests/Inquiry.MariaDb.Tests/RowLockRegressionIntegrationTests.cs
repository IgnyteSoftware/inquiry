using Inquiry.FeatureCatalog.LockingRegression;
using Inquiry.MariaDb.Tests.Fixtures;
using Inquiry.Tests.Shared;

namespace Inquiry.MariaDb.Tests;

/// <summary>
/// Live row-locking regressions on MariaDB: a locking projection read holds the row (#539), and the lock shapes the generator accepts run (#538).
/// </summary>
[Collection(MariaDbCollection.Name)]
public sealed class RowLockRegressionIntegrationTests
{
    private readonly MariaDbContainerFixture _fixture;
    public RowLockRegressionIntegrationTests(MariaDbContainerFixture fixture) => _fixture = fixture;

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

    [SkippableFact]
    public async Task DistinctLockModesRun()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await CreateHarnessAsync();
        await RowLockRegressionDistinctScenarios.DistinctLocksRunAsync(harness.Services);
    }

    private Task<MariaDbTestHarness> CreateHarnessAsync()
        => MariaDbTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, LockProbeSchema.MySqlDdl, "lockreg", configureServices: LockRegressionCommandCounter.Register);
}
