using Inquiry.Commands;
using Inquiry.FeatureCatalog.LockingRegression;
using Inquiry.Interceptors;
using Microsoft.Extensions.DependencyInjection;

namespace Inquiry.Tests.Shared;

/// <summary>
/// Live checks that a projection read with a lock mode holds the row (#539). A holder transaction reads
/// a <see cref="LockProbeSummary"/> projection, then a second connection updates the same row. Each side
/// uses its own DI scope, so each has its own connection.
/// </summary>
internal static class RowLockRegressionScenarios
{
    // A writer that has not finished after this long is treated as blocked by the held lock.
    private static readonly TimeSpan _blockedFor = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan _completesWithin = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Reads through <c>SelectAllByField</c> when <paramref name="byField"/>, otherwise <c>SelectAll</c>.
    /// With <paramref name="locked"/>, the writer waits until the holder commits. Without it, the
    /// writer finishes while the holder transaction is still open, which shows the wait comes from the
    /// lock clause and not from the engine's default isolation.
    /// </summary>
    internal static async Task ProjectionReadHoldsTheRowOnlyWhenLockedAsync(IServiceProvider services, bool byField, bool locked)
    {
        await SeedAsync(services, new LockProbe { Id = 1, State = 1 });

        await using var holder = services.CreateAsyncScope();
        var inquiry = holder.ServiceProvider.GetRequiredService<IInquiry>();
        var probes = holder.ServiceProvider.GetRequiredService<LockProbeStore>();
        Task<bool> writer;
        await using (var transaction = await inquiry.BeginTransactionAsync())
        {
            var rows = (byField, locked) switch
            {
                (false, true) => await probes.SummariesForUpdateAsync(),
                (false, false) => await probes.SummariesAsync(),
                (true, true) => await probes.SummariesByStateForUpdateAsync(1),
                (true, false) => await probes.SummariesByStateAsync(1),
            };
            Assert.Equal(1, Assert.Single(rows).Id);

            writer = await StartAsync(services, () => UpdateAsync(services, new LockProbe { Id = 1, State = 1, Attempts = 1 }));
            if (locked)
            {
                await AssertBlockedAsync(writer);
            }
            else
            {
                Assert.True(await writer.WaitAsync(_completesWithin));
            }

            await transaction.CommitAsync();
        }

        Assert.True(await writer.WaitAsync(_completesWithin));
        Assert.Equal(1, (await GetAsync(services, 1)).Attempts);
    }

    /// <summary>
    /// <c>[InquirySelectOneByField]</c> (#609): one match returns the row, no match returns null, and two
    /// matches throw with the store method and field in the message.
    /// </summary>
    internal static async Task SelectOneByFieldEnforcesCardinalityAsync(IServiceProvider services)
    {
        await SeedAsync(services, ShapeRows());

        await using var scope = services.CreateAsyncScope();
        var probes = scope.ServiceProvider.GetRequiredService<LockProbeStore>();
        Assert.Equal(5, (await probes.GetByStateAsync(3))?.Id);
        Assert.Null(await probes.GetByStateAsync(9));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => probes.GetByStateAsync(1));
        Assert.Contains("'LockProbeStore.GetByStateAsync'", error.Message, StringComparison.Ordinal);
        Assert.Contains("'State'", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A locked <c>[InquirySelectOneByField]</c> read holds its row: the writer waits until the holder
    /// commits. The entity and projection reads build their SQL separately, so each is run.
    /// </summary>
    internal static async Task SelectOneByFieldLockHoldsTheRowAsync(IServiceProvider services, bool projection)
    {
        await SeedAsync(services, new LockProbe { Id = 1, State = 1 });

        await using var holder = services.CreateAsyncScope();
        var inquiry = holder.ServiceProvider.GetRequiredService<IInquiry>();
        var probes = holder.ServiceProvider.GetRequiredService<LockProbeStore>();
        Task<bool> writer;
        await using (var transaction = await inquiry.BeginTransactionAsync())
        {
            var id = projection
                ? (await probes.SummaryByStateForUpdateAsync(1))?.Id
                : (await probes.GetByStateForUpdateAsync(1))?.Id;
            Assert.Equal(1, id);

            writer = await StartAsync(services, () => UpdateAsync(services, new LockProbe { Id = 1, State = 1, Attempts = 1 }));
            await AssertBlockedAsync(writer);
            await transaction.CommitAsync();
        }

        Assert.True(await writer.WaitAsync(_completesWithin));
        Assert.Equal(1, (await GetAsync(services, 1)).Attempts);
    }

    /// <summary>Five rows over three states, for the paging and DISTINCT checks.</summary>
    internal static LockProbe[] ShapeRows() =>
    [
        new() { Id = 1, State = 1 },
        new() { Id = 2, State = 1 },
        new() { Id = 3, State = 2 },
        new() { Id = 4, State = 2 },
        new() { Id = 5, State = 3 },
    ];

    internal static async Task SeedAsync(IServiceProvider services, params LockProbe[] probes)
    {
        await using var scope = services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<LockProbeStore>();
        foreach (var probe in probes)
        {
            Assert.NotNull(await store.InsertAsync(probe));
        }
    }

    internal static async Task<LockProbe> GetAsync(IServiceProvider services, long id)
    {
        await using var scope = services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<LockProbeStore>();
        return await store.GetAsync(id) ?? throw new InvalidOperationException($"Lock probe {id} is missing.");
    }

    private static async Task<bool> UpdateAsync(IServiceProvider services, LockProbe probe)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<LockProbeStore>().UpdateAsync(probe);
    }

    /// <summary>
    /// Starts <paramref name="work"/> on its own async flow and returns once it has sent its first
    /// command, so a later blocked check measures the database, not a slow start.
    /// </summary>
    internal static async Task<Task<T>> StartAsync<T>(IServiceProvider services, Func<Task<T>> work)
    {
        var commands = services.GetRequiredService<LockRegressionCommandCounter>();
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
            Assert.Fail("The writer finished while another transaction held the lock.");
        }
    }
}

/// <summary>
/// Counts commands as they start. Register it with <see cref="Register"/> so a scenario can tell when a
/// worker has sent its query.
/// </summary>
internal sealed class LockRegressionCommandCounter : IInquiryCommandInterceptor
{
    private int _count;

    public int Count => Volatile.Read(ref _count);

    public static void Register(IServiceCollection services)
    {
        var counter = new LockRegressionCommandCounter();
        services.AddSingleton(counter);
        services.AddSingleton<IInquiryCommandInterceptor>(counter);
    }

    public ValueTask CommandExecutingAsync(InquiryCommandContext context, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _count);
        return ValueTask.CompletedTask;
    }
}
