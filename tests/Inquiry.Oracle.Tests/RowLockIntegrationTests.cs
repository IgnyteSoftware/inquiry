using Inquiry.FeatureCatalog.Locking;
using Inquiry.Oracle.Tests.Fixtures;
using Inquiry.Tests.Shared;
using Oracle.ManagedDataAccess.Client;

namespace Inquiry.Oracle.Tests;

/// <summary>
/// Runs the row-locking guide's examples and provider matrix (#402) against a live engine. The
/// assertions here are the observed behaviour that the guide documents for this provider.
/// </summary>
[Collection(OracleCollection.Name)]
public sealed class RowLockIntegrationTests
{
    private readonly OracleContainerFixture _fixture;
    public RowLockIntegrationTests(OracleContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task RecordAttemptWaitsForTheLockAndLosesNoUpdate()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await CreateHarnessAsync();
        await RowLockScenarios.RecordAttemptWaitsForTheLockAndLosesNoUpdateAsync(harness.Services);
    }

    [SkippableFact]
    public async Task NoWaitFailsWhileHeldAndSucceedsAfterRollback()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await CreateHarnessAsync();
        var conflict = await RowLockScenarios.NoWaitFailsWhileHeldAndSucceedsAfterRollbackAsync(harness.Services);
        var error = Assert.IsType<OracleException>(conflict);
        Assert.Equal(54, error.Number);
    }

    [SkippableFact]
    public async Task ClaimAvailableSkipsLockedRows()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await CreateHarnessAsync();
        await RowLockScenarios.ClaimAvailableSkipsLockedRowsAsync(harness.Services);
    }

    [SkippableFact]
    public async Task LockOutsideATransactionIsNotRetained()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await CreateHarnessAsync();
        Assert.Null(await RowLockScenarios.LockOutsideATransactionIsNotRetainedAsync(harness.Services));
    }

    private Task<OracleTestHarness> CreateHarnessAsync()
        => OracleTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, LockingSchema.OracleDdl, "lock");
}
