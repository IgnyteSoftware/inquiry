using Inquiry.FeatureCatalog.LockingRegression;
using Inquiry.SqlServer.Tests.Fixtures;
using Inquiry.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace Inquiry.SqlServer.Tests;

/// <summary>
/// Live row-locking regressions on SQL Server: a locking projection read holds the row (#539), and the lock shapes the generator accepts run (#538).
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class RowLockRegressionIntegrationTests
{
    private readonly SqlServerContainerFixture _fixture;
    public RowLockRegressionIntegrationTests(SqlServerContainerFixture fixture) => _fixture = fixture;

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

    /// <summary>
    /// The <c>Status</c> table has a <c>Status</c> column (#540). Every lockable operation and lock mode
    /// runs, and the Update lock makes a writer wait. Generator tests cover a schema-qualified table: the
    /// generated schema DDL that other tests run does not create database schemas.
    /// </summary>
    [SkippableFact]
    public async Task ColumnNamedLikeItsTableRunsLockedSelects()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await CreateHarnessAsync();
        var services = harness.Services;
        await using (var seed = services.CreateAsyncScope())
        {
            var rows = seed.ServiceProvider.GetRequiredService<StatusRowStore>();
            Assert.NotNull(await rows.InsertAsync(new StatusRow { Id = 1, Code = 7 }));
            Assert.NotNull(await rows.InsertAsync(new StatusRow { Id = 2, Code = 8 }));
        }

        await using (var reader = services.CreateAsyncScope())
        {
            var inquiry = reader.ServiceProvider.GetRequiredService<IInquiry>();
            var rows = reader.ServiceProvider.GetRequiredService<StatusRowStore>();
            await inquiry.ExecuteInTransactionAsync(async _ =>
            {
                Assert.Equal(7, (await rows.GetForUpdateAsync(1))?.Code);
                Assert.Equal(2, (await rows.AllForShareAsync()).Count);
                Assert.Equal(1, Assert.Single(await rows.ByCodeNoWaitAsync(7)).Id);
                Assert.Equal(2, Assert.Single(await rows.WhereCodeSkipLockedAsync(8)).Id);
                Assert.Equal(7, Assert.Single(await rows.CodesForUpdateAsync(7)).Code);
            });
        }

        await using var holder = services.CreateAsyncScope();
        var holderInquiry = holder.ServiceProvider.GetRequiredService<IInquiry>();
        Task<bool> writer;
        await using (var transaction = await holderInquiry.BeginTransactionAsync())
        {
            Assert.NotNull(await holder.ServiceProvider.GetRequiredService<StatusRowStore>().GetForUpdateAsync(1));
            writer = await RowLockRegressionScenarios.StartAsync(services, async () =>
            {
                await using var scope = services.CreateAsyncScope();
                return await scope.ServiceProvider.GetRequiredService<StatusRowStore>().UpdateAsync(new StatusRow { Id = 1, Code = 9 });
            });
            await RowLockRegressionScenarios.AssertBlockedAsync(writer);
            await transaction.CommitAsync();
        }

        Assert.True(await writer.WaitAsync(TimeSpan.FromSeconds(30)));
    }

    private Task<SqlServerTestHarness> CreateHarnessAsync()
        => SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, LockProbeSchema.SqlServerDdl + StatusRowSchema.SqlServerDdl, "lockreg", configureServices: LockRegressionCommandCounter.Register);
}
