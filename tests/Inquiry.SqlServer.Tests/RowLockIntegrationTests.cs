using Inquiry.FeatureCatalog.Locking;
using Inquiry.SqlServer.Tests.Fixtures;
using Inquiry.Tests.Shared;
using Microsoft.Data.SqlClient;

namespace Inquiry.SqlServer.Tests;

/// <summary>
/// Runs the row-locking guide's examples and provider matrix (#402) against a live engine. The
/// assertions here are the observed behaviour that the guide documents for this provider.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class RowLockIntegrationTests
{
    private readonly SqlServerContainerFixture _fixture;
    public RowLockIntegrationTests(SqlServerContainerFixture fixture) => _fixture = fixture;

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
        var error = Assert.IsType<SqlException>(conflict);
        Assert.Equal(1222, error.Number);
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
        Assert.Null(noWait);
    }

    private Task<SqlServerTestHarness> CreateHarnessAsync()
        => SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, LockingSchema.SqlServerDdl, "lock");
}
