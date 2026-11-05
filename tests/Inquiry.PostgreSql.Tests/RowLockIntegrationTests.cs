using Inquiry.FeatureCatalog.Locking;
using Inquiry.PostgreSql.Tests.Fixtures;
using Inquiry.Tests.Shared;
using Npgsql;

namespace Inquiry.PostgreSql.Tests;

/// <summary>
/// Runs the row-locking guide's examples and provider matrix (#402) against a live engine. The
/// assertions here are the observed behaviour that the guide documents for this provider.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
public sealed class RowLockIntegrationTests
{
    private readonly PostgreSqlContainerFixture _fixture;
    public RowLockIntegrationTests(PostgreSqlContainerFixture fixture) => _fixture = fixture;

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
        var error = Assert.IsType<PostgresException>(conflict);
        Assert.Equal("55P03", error.SqlState);
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
        Assert.Equal("55P03", Assert.IsType<PostgresException>(noWait).SqlState);
    }

    private Task<PostgreSqlTestHarness> CreateHarnessAsync()
        => PostgreSqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, LockingSchema.PostgreSqlDdl, "lock", configureServices: CommandStartCounter.Register);
}
