using Inquiry.FeatureCatalog.LockingRegression;
using Inquiry.Oracle.Tests.Fixtures;
using Inquiry.Tests.Shared;

namespace Inquiry.Oracle.Tests;

/// <summary>
/// Live row-locking regressions on Oracle: a locking projection read holds the row (#539). DISTINCT and OFFSET/FETCH paging with a lock are rejected at build time (INQ039), so they have no live check here.
/// </summary>
[Collection(OracleCollection.Name)]
public sealed class RowLockRegressionIntegrationTests
{
    private readonly OracleContainerFixture _fixture;
    public RowLockRegressionIntegrationTests(OracleContainerFixture fixture) => _fixture = fixture;

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

    private Task<OracleTestHarness> CreateHarnessAsync()
        => OracleTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, LockProbeSchema.OracleDdl, "lockreg", configureServices: LockRegressionCommandCounter.Register);
}
