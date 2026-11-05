using Fulfillment;
using Inquiry.FeatureCatalog.Locking;
using Microsoft.Extensions.DependencyInjection;

namespace Inquiry.Tests.Shared;

/// <summary>
/// The <c>Share</c> row-lock scenario behind the row-locking guide (#402). Oracle rejects Share at
/// compile time, so only the PostgreSQL, MySQL, MariaDB and SQL Server test projects link this file.
/// </summary>
internal static class RowLockShareScenarios
{
    private static readonly TimeSpan _completesWithin = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Two transactions hold a Share lock on the same row at once. A writer waits until both end.
    /// Also returns the outcome of an UpdateNoWait read taken while the Share locks are held: null when
    /// it succeeded, otherwise its exception.
    /// </summary>
    internal static async Task<Exception?> ShareAllowsReadersAndBlocksWritersAsync(IServiceProvider services)
    {
        var id = await RowLockScenarios.SeedAsync(services, queueId: 1);

        await using var first = services.CreateAsyncScope();
        await using var second = services.CreateAsyncScope();
        var firstInquiry = first.ServiceProvider.GetRequiredService<IInquiry>();
        var secondInquiry = second.ServiceProvider.GetRequiredService<IInquiry>();

        Task<bool> write;
        Exception? noWaitOutcome = null;
        await using (var firstTransaction = await firstInquiry.BeginTransactionAsync())
        await using (var secondTransaction = await secondInquiry.BeginTransactionAsync())
        {
            Assert.NotNull(await first.ServiceProvider.GetRequiredService<WorkItemShareStore>().GetForShareAsync(id));
            Assert.NotNull(await second.ServiceProvider.GetRequiredService<WorkItemShareStore>().GetForShareAsync(id)
                .WaitAsync(_completesWithin));

            try
            {
                await Task.Run(() => RowLockScenarios.ReadNoWaitInTransactionAsync(services, id)).WaitAsync(_completesWithin);
            }
            catch (Exception ex) when (ex is not TimeoutException)
            {
                noWaitOutcome = ex;
            }

            write = Task.Run(() => WorkItemService.RecordAttemptAsync(services, id));
            await RowLockScenarios.AssertBlockedAsync(write);
            await secondTransaction.CommitAsync();
            await RowLockScenarios.AssertBlockedAsync(write);
            await firstTransaction.CommitAsync();
        }

        Assert.True(await write.WaitAsync(_completesWithin));
        Assert.Equal(1, (await RowLockScenarios.GetAsync(services, id)).Attempts);
        return noWaitOutcome;
    }
}
