using Inquiry.FeatureCatalog.Locking;
using Inquiry.MySql.Tests.Fixtures;
using Inquiry.Tests.Shared;
using MySqlConnector;

namespace Inquiry.MySql.Tests;

/// <summary>
/// Runs the row-locking guide's examples and provider matrix (#402) against a live engine. The
/// assertions here are the observed behaviour that the guide documents for this provider.
/// </summary>
[Collection(MySqlCollection.Name)]
public sealed class RowLockIntegrationTests
{
    private readonly MySqlContainerFixture _fixture;
    public RowLockIntegrationTests(MySqlContainerFixture fixture) => _fixture = fixture;

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
        var error = Assert.IsType<MySqlException>(conflict);
        Assert.Equal(3572, error.Number);
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

    [SkippableFact]
    public async Task ShareAllowsReadersAndBlocksWriters()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await CreateHarnessAsync();
        var noWait = await RowLockShareScenarios.ShareAllowsReadersAndBlocksWritersAsync(harness.Services);
        Assert.Equal(3572, Assert.IsType<MySqlException>(noWait).Number);
    }

    private Task<MySqlTestHarness> CreateHarnessAsync()
        => MySqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, LockingSchema.MySqlDdl, "lock");
}
