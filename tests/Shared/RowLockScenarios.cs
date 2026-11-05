using Fulfillment;
using Inquiry.Commands;
using Inquiry.FeatureCatalog.Locking;
using Inquiry.Interceptors;
using Microsoft.Extensions.DependencyInjection;

namespace Inquiry.Tests.Shared;

/// <summary>
/// Live row-locking scenarios behind the row-locking guide (#402). Each scenario runs the guide's
/// published <see cref="WorkItemService"/> code against a second transaction that holds a lock, so the
/// result depends on the engine actually honouring the generated lock clause. Every worker uses its
/// own DI scope, which gives it its own connection and transaction.
/// </summary>
internal static class RowLockScenarios
{
    // A worker that has not finished after this long is treated as blocked by the held lock.
    private static readonly TimeSpan _blockedFor = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan _completesWithin = TimeSpan.FromSeconds(30);

    /// <summary>
    /// <see cref="WorkItemService.RecordAttemptAsync"/> waits while another transaction holds the row, then
    /// reads the committed value. Both increments survive. The holder writes only after the worker is
    /// blocked, so an unlocked read would return the old value and lose an increment.
    /// </summary>
    internal static async Task RecordAttemptWaitsForTheLockAndLosesNoUpdateAsync(IServiceProvider services)
    {
        var id = await SeedAsync(services, queueId: 1);

        await using var holder = services.CreateAsyncScope();
        var inquiry = holder.ServiceProvider.GetRequiredService<IInquiry>();
        var items = holder.ServiceProvider.GetRequiredService<WorkItemStore>();
        Task<bool> recorded;
        await using (var transaction = await inquiry.BeginTransactionAsync())
        {
            var held = await items.GetForUpdateAsync(id) ?? throw new InvalidOperationException("Seeded row is missing.");

            recorded = await StartAsync(services, () => WorkItemService.RecordAttemptAsync(services, id));
            await AssertBlockedAsync(recorded);

            held.Attempts++;
            Assert.True(await items.UpdateAsync(held));
            await transaction.CommitAsync();
        }

        Assert.True(await recorded.WaitAsync(_completesWithin));
        Assert.Equal(2, (await GetAsync(services, id)).Attempts);
    }

    /// <summary>
    /// While a transaction holds an Update lock, an UpdateNoWait read fails at once. After the holder
    /// rolls back, the same read succeeds. Returns the conflict exception for provider assertions.
    /// </summary>
    internal static async Task<Exception> NoWaitFailsWhileHeldAndSucceedsAfterRollbackAsync(IServiceProvider services)
    {
        var id = await SeedAsync(services, queueId: 1);

        Exception conflict;
        await using (var holder = services.CreateAsyncScope())
        {
            var inquiry = holder.ServiceProvider.GetRequiredService<IInquiry>();
            var items = holder.ServiceProvider.GetRequiredService<WorkItemStore>();
            await using var transaction = await inquiry.BeginTransactionAsync();
            Assert.NotNull(await items.GetForUpdateAsync(id));

            conflict = await Assert.ThrowsAnyAsync<Exception>(
                () => Task.Run(() => ReadNoWaitInTransactionAsync(services, id)).WaitAsync(_completesWithin));

            // Disposing without committing rolls back and releases the lock.
        }

        Assert.NotNull(await ReadNoWaitInTransactionAsync(services, id));
        return conflict;
    }

    /// <summary>
    /// <see cref="WorkItemService.ClaimAvailableAsync"/> skips a row another transaction holds and claims
    /// the rest of its queue only. The skipped row is claimable once the holder ends.
    /// </summary>
    internal static async Task ClaimAvailableSkipsLockedRowsAsync(IServiceProvider services)
    {
        var held = await SeedAsync(services, queueId: 1);
        var free1 = await SeedAsync(services, queueId: 1);
        var free2 = await SeedAsync(services, queueId: 1);
        var otherQueue = await SeedAsync(services, queueId: 2);

        await using (var holder = services.CreateAsyncScope())
        {
            var inquiry = holder.ServiceProvider.GetRequiredService<IInquiry>();
            var items = holder.ServiceProvider.GetRequiredService<WorkItemStore>();
            await using var transaction = await inquiry.BeginTransactionAsync();
            Assert.NotNull(await items.GetForUpdateAsync(held));

            var claimed = await Task.Run(() => WorkItemService.ClaimAvailableAsync(services, 1)).WaitAsync(_completesWithin);
            Assert.Equal(new[] { free1, free2 }, claimed.Order());

            // Every pending row in queue 1 is now claimed or held, so a second claim gets nothing.
            Assert.Empty(await Task.Run(() => WorkItemService.ClaimAvailableAsync(services, 1)).WaitAsync(_completesWithin));
        }

        Assert.Equal(WorkItemService.Claimed, (await GetAsync(services, free1)).State);
        Assert.Equal(WorkItemService.Claimed, (await GetAsync(services, free2)).State);
        Assert.Equal(WorkItemService.Pending, (await GetAsync(services, otherQueue)).State);
        Assert.Equal(new[] { held }, await WorkItemService.ClaimAvailableAsync(services, 1));
        Assert.Empty(await WorkItemService.ClaimAvailableAsync(services, 1));
    }

    /// <summary>
    /// Calls an Update-locking method with no transaction, then checks whether another transaction can
    /// lock the same row. Returns the first call's exception, or null when it succeeded.
    /// </summary>
    internal static async Task<Exception?> LockOutsideATransactionIsNotRetainedAsync(IServiceProvider services)
    {
        var id = await SeedAsync(services, queueId: 1);

        Exception? outcome = null;
        await using (var scope = services.CreateAsyncScope())
        {
            var items = scope.ServiceProvider.GetRequiredService<WorkItemStore>();
            try
            {
                Assert.NotNull(await items.GetForUpdateAsync(id));
            }
            catch (Exception ex)
            {
                outcome = ex;
            }

            // The scope is still alive, yet no lock outlives the call.
            Assert.NotNull(await Task.Run(() => ReadNoWaitInTransactionAsync(services, id)).WaitAsync(_completesWithin));
        }

        return outcome;
    }

    internal static async Task<long> SeedAsync(IServiceProvider services, int queueId)
    {
        await using var scope = services.CreateAsyncScope();
        var probe = scope.ServiceProvider.GetRequiredService<WorkItemProbeStore>();
        var item = await probe.InsertReturningAsync(new WorkItem { QueueId = queueId, State = WorkItemService.Pending })
            ?? throw new InvalidOperationException("The insert returned no row.");
        return item.Id;
    }

    internal static async Task<WorkItem> GetAsync(IServiceProvider services, long id)
    {
        await using var scope = services.CreateAsyncScope();
        var probe = scope.ServiceProvider.GetRequiredService<WorkItemProbeStore>();
        return await probe.GetAsync(id) ?? throw new InvalidOperationException($"Work item {id} is missing.");
    }

    internal static async Task<WorkItem?> ReadNoWaitInTransactionAsync(IServiceProvider services, long id)
    {
        await using var scope = services.CreateAsyncScope();
        var inquiry = scope.ServiceProvider.GetRequiredService<IInquiry>();
        var probe = scope.ServiceProvider.GetRequiredService<WorkItemProbeStore>();
        return await inquiry.ExecuteInTransactionAsync(_ => probe.GetForUpdateNoWaitAsync(id));
    }

    /// <summary>
    /// Starts <paramref name="work"/> on its own async flow and returns once it has sent its first
    /// command, so a later blocked check measures the database, not a slow start.
    /// </summary>
    internal static async Task<Task<T>> StartAsync<T>(IServiceProvider services, Func<Task<T>> work)
    {
        var commands = services.GetRequiredService<CommandStartCounter>();
        var before = commands.Count;
        var worker = Task.Run(work);
        using var timeout = new CancellationTokenSource(_completesWithin);
        while (commands.Count == before && !worker.IsCompleted)
        {
            await Task.Delay(10, timeout.Token);
        }

        return worker;
    }

    internal static async Task AssertBlockedAsync(Task worker)
    {
        await Task.WhenAny(worker, Task.Delay(_blockedFor));
        if (worker.IsCompleted)
        {
            // Surface the worker's own failure, if any, before reporting that it did not block.
            await worker;
            Assert.Fail("The worker finished while another transaction held the lock.");
        }
    }
}

/// <summary>
/// Counts commands as they start. Register it with <see cref="Register"/> so a scenario can tell when a
/// worker has sent its query.
/// </summary>
internal sealed class CommandStartCounter : IInquiryCommandInterceptor
{
    private int _count;

    public int Count => Volatile.Read(ref _count);

    public static void Register(IServiceCollection services)
    {
        var counter = new CommandStartCounter();
        services.AddSingleton(counter);
        services.AddSingleton<IInquiryCommandInterceptor>(counter);
    }

    public ValueTask CommandExecutingAsync(InquiryCommandContext context, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _count);
        return ValueTask.CompletedTask;
    }
}
