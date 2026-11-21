using System.Diagnostics;
using Inquiry.SqlServer.Tests.Fixtures;
using Inquiry.Transactions;
using Microsoft.Extensions.DependencyInjection;

namespace Inquiry.SqlServer.Tests;

/// <summary>
/// Runs <see cref="SqlServerInquiryTransactionExtensions.AcquireAppLockAsync"/> against a live engine (#611).
/// Each transaction uses its own DI scope, so it has its own connection.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class AppLockIntegrationTests
{
    private static readonly TimeSpan _blockedFor = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan _completesWithin = TimeSpan.FromSeconds(30);

    private readonly SqlServerContainerFixture _fixture;
    public AppLockIntegrationTests(SqlServerContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task ExclusiveWaiterTimesOutWhileHeld()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateAsync(_fixture.AdminConnectionString, "AppLock");
        const string resource = "applock:timeout";

        await using var holder = await BeginAsync(harness);
        await holder.Transaction.AcquireAppLockAsync(resource, SqlServerAppLockMode.Exclusive, TimeSpan.Zero);
        Assert.Equal("Exclusive", await LockModeAsync(holder.Transaction, resource));

        var elapsed = Stopwatch.StartNew();
        var error = await Assert.ThrowsAsync<SqlServerAppLockException>(
            () => Task.Run(() => AcquireInNewTransactionAsync(harness, resource, SqlServerAppLockMode.Exclusive, TimeSpan.FromMilliseconds(500))).WaitAsync(_completesWithin));
        elapsed.Stop();

        Assert.Equal(SqlServerAppLockFailure.Timeout, error.Failure);
        Assert.Equal(-1, error.ReturnCode);
        Assert.Equal(resource, error.Resource);
        Assert.True(elapsed.Elapsed >= TimeSpan.FromMilliseconds(450), $"The waiter returned after {elapsed.Elapsed}.");
    }

    [SkippableFact]
    public async Task ExclusiveWaiterIsGrantedWhenHolderCommits()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateAsync(_fixture.AdminConnectionString, "AppLock");
        const string resource = "applock:commit";

        // Open the waiter first, so the blocked check covers the lock wait and not the connection open.
        await using var waiter = await BeginAsync(harness);
        Task waiting;
        await using (var holder = await BeginAsync(harness))
        {
            await holder.Transaction.AcquireAppLockAsync(resource, SqlServerAppLockMode.Exclusive, TimeSpan.Zero);
            waiting = Task.Run(() => waiter.Transaction.AcquireAppLockAsync(resource, SqlServerAppLockMode.Exclusive, _completesWithin));
            await AssertBlockedAsync(waiting);
            await holder.Transaction.CommitAsync();
        }

        await waiting.WaitAsync(_completesWithin);
        Assert.Equal("Exclusive", await LockModeAsync(waiter.Transaction, resource));
    }

    [SkippableFact]
    public async Task LockIsReleasedWhenHolderRollsBack()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateAsync(_fixture.AdminConnectionString, "AppLock");
        const string resource = "applock:rollback";

        await using (var holder = await BeginAsync(harness))
        {
            await holder.Transaction.AcquireAppLockAsync(resource, SqlServerAppLockMode.Exclusive, TimeSpan.Zero);
            await holder.Transaction.RollbackAsync();
        }

        Assert.Equal("Exclusive", await AcquireInNewTransactionAsync(harness, resource, SqlServerAppLockMode.Exclusive, TimeSpan.Zero));

        // Disposal without commit is the other rollback path.
        await using (var holder = await BeginAsync(harness))
        {
            await holder.Transaction.AcquireAppLockAsync(resource, SqlServerAppLockMode.Exclusive, TimeSpan.Zero);
        }

        Assert.Equal("Exclusive", await AcquireInNewTransactionAsync(harness, resource, SqlServerAppLockMode.Exclusive, TimeSpan.Zero));
    }

    [SkippableFact]
    public async Task SavepointRollbackReleasesItsLockAndCommitKeepsIt()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateAsync(_fixture.AdminConnectionString, "AppLock");
        const string rolledBack = "applock:savepoint-rollback";
        const string committed = "applock:savepoint-commit";

        await using var holder = await BeginAsync(harness);
        await using (var savepoint = await holder.Transaction.BeginTransactionAsync())
        {
            await savepoint.AcquireAppLockAsync(rolledBack, SqlServerAppLockMode.Exclusive, TimeSpan.Zero);
            await savepoint.RollbackAsync();
        }

        await using (var savepoint = await holder.Transaction.BeginTransactionAsync())
        {
            await savepoint.AcquireAppLockAsync(committed, SqlServerAppLockMode.Exclusive, TimeSpan.Zero);
            await savepoint.CommitAsync();
        }

        Assert.Equal("NoLock", await LockModeAsync(holder.Transaction, rolledBack));
        Assert.Equal("Exclusive", await Task.Run(() => AcquireInNewTransactionAsync(harness, rolledBack, SqlServerAppLockMode.Exclusive, TimeSpan.Zero)).WaitAsync(_completesWithin));

        Assert.Equal("Exclusive", await LockModeAsync(holder.Transaction, committed));
        var error = await Assert.ThrowsAsync<SqlServerAppLockException>(
            () => Task.Run(() => AcquireInNewTransactionAsync(harness, committed, SqlServerAppLockMode.Exclusive, TimeSpan.Zero)).WaitAsync(_completesWithin));
        Assert.Equal(SqlServerAppLockFailure.Timeout, error.Failure);
    }

    [SkippableFact]
    public async Task SavepointRollbackKeepsAConvertedMode()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateAsync(_fixture.AdminConnectionString, "AppLock");
        const string resource = "applock:savepoint-convert";

        await using var holder = await BeginAsync(harness);
        await holder.Transaction.AcquireAppLockAsync(resource, SqlServerAppLockMode.Shared, TimeSpan.Zero);
        await using (var savepoint = await holder.Transaction.BeginTransactionAsync())
        {
            await savepoint.AcquireAppLockAsync(resource, SqlServerAppLockMode.Exclusive, TimeSpan.Zero);
            await savepoint.RollbackAsync();
        }

        Assert.Equal("Exclusive", await LockModeAsync(holder.Transaction, resource));
    }

    [SkippableFact]
    public async Task SharedHoldersCoexistAndBlockExclusive()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateAsync(_fixture.AdminConnectionString, "AppLock");
        const string resource = "applock:shared";

        await using var first = await BeginAsync(harness);
        await first.Transaction.AcquireAppLockAsync(resource, SqlServerAppLockMode.Shared, TimeSpan.Zero);
        await using var second = await BeginAsync(harness);
        await second.Transaction.AcquireAppLockAsync(resource, SqlServerAppLockMode.Shared, TimeSpan.Zero);

        var error = await Assert.ThrowsAsync<SqlServerAppLockException>(
            () => Task.Run(() => AcquireInNewTransactionAsync(harness, resource, SqlServerAppLockMode.Exclusive, TimeSpan.Zero)).WaitAsync(_completesWithin));
        Assert.Equal(SqlServerAppLockFailure.Timeout, error.Failure);

        // A refused upgrade keeps the Shared lock the transaction already held.
        var upgrade = await Assert.ThrowsAsync<SqlServerAppLockException>(
            () => first.Transaction.AcquireAppLockAsync(resource, SqlServerAppLockMode.Exclusive, TimeSpan.Zero));
        Assert.Equal(SqlServerAppLockFailure.Timeout, upgrade.Failure);
        Assert.Equal("Shared", await LockModeAsync(first.Transaction, resource));
    }

    [SkippableFact]
    public async Task DeadlockVictimGetsDeadlockFailure()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateAsync(_fixture.AdminConnectionString, "AppLock");
        using var bothHoldFirstLock = new Barrier(2);

        async Task<SqlServerAppLockException?> RunAsync(string first, string second)
        {
            await using var worker = await BeginAsync(harness);
            await worker.Transaction.AcquireAppLockAsync(first, SqlServerAppLockMode.Exclusive, TimeSpan.Zero);
            Assert.True(bothHoldFirstLock.SignalAndWait(_completesWithin), "The other worker did not take its first lock.");
            try
            {
                await worker.Transaction.AcquireAppLockAsync(second, SqlServerAppLockMode.Exclusive, _completesWithin);
                return null;
            }
            catch (SqlServerAppLockException error)
            {
                // The victim's transaction stays open and still holds its first lock.
                Assert.Equal(1, await worker.Transaction.ExecuteScalarAsync<int>($"SELECT @@TRANCOUNT"));
                Assert.Equal("Exclusive", await LockModeAsync(worker.Transaction, first));

                // Disposal rolls back and releases the first lock, so the other worker can finish.
                return error;
            }
        }

        var outcomes = await Task.WhenAll(
            Task.Run(() => RunAsync("applock:a", "applock:b")),
            Task.Run(() => RunAsync("applock:b", "applock:a"))).WaitAsync(_completesWithin * 2);

        var failure = Assert.Single(outcomes, outcome => outcome is not null);
        Assert.Equal(SqlServerAppLockFailure.Deadlock, failure!.Failure);
        Assert.Equal(-3, failure.ReturnCode);
    }

    [SkippableFact]
    public async Task CallerCancellationStopsTheWait()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateAsync(_fixture.AdminConnectionString, "AppLock");
        const string resource = "applock:cancel";

        await using var holder = await BeginAsync(harness);
        await holder.Transaction.AcquireAppLockAsync(resource, SqlServerAppLockMode.Exclusive, TimeSpan.Zero);

        // Open the waiter first, so the token cancels a wait that is already on the server.
        await using var waiter = await BeginAsync(harness);
        using var cancel = new CancellationTokenSource();
        var elapsed = Stopwatch.StartNew();
        cancel.CancelAfter(TimeSpan.FromMilliseconds(500));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Task.Run(() => waiter.Transaction.AcquireAppLockAsync(resource, SqlServerAppLockMode.Exclusive, Timeout.InfiniteTimeSpan, cancel.Token)).WaitAsync(_completesWithin));
        Assert.True(elapsed.Elapsed >= TimeSpan.FromMilliseconds(450), $"The wait ended after {elapsed.Elapsed}.");
    }

    private static async Task<ScopedTransaction> BeginAsync(SqlServerTestHarness harness)
    {
        var scope = harness.Services.CreateAsyncScope();
        try
        {
            var transaction = await scope.ServiceProvider.GetRequiredService<IInquiry>().BeginTransactionAsync();
            return new ScopedTransaction(scope, transaction);
        }
        catch
        {
            await scope.DisposeAsync();
            throw;
        }
    }

    // Returns the mode the new transaction holds after the call, as SQL Server reports it.
    private static async Task<string> AcquireInNewTransactionAsync(
        SqlServerTestHarness harness, string resource, SqlServerAppLockMode mode, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        await using var worker = await BeginAsync(harness);
        await worker.Transaction.AcquireAppLockAsync(resource, mode, timeout, cancellationToken);
        var held = await LockModeAsync(worker.Transaction, resource);
        await worker.Transaction.CommitAsync(CancellationToken.None);
        return held;
    }

    private static Task<string> LockModeAsync(IInquiryTransaction transaction, string resource)
        => transaction.ExecuteScalarAsync<string>($"SELECT APPLOCK_MODE('public', {resource}, 'Transaction')");

    private static async Task AssertBlockedAsync(Task worker)
    {
        await Task.WhenAny(worker, Task.Delay(_blockedFor));
        if (worker.IsCompleted)
        {
            await worker;
            Assert.Fail("The waiter finished while another transaction held the lock.");
        }
    }

    private sealed class ScopedTransaction(AsyncServiceScope scope, IInquiryTransaction transaction) : IAsyncDisposable
    {
        public IInquiryTransaction Transaction { get; } = transaction;

        public async ValueTask DisposeAsync()
        {
            await Transaction.DisposeAsync();
            await scope.DisposeAsync();
        }
    }
}
