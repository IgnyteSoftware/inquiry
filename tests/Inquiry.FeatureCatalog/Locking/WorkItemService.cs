using Inquiry;
using Microsoft.Extensions.DependencyInjection;

namespace Fulfillment;

public static class WorkItemService
{
    public const int Pending = 0;
    public const int Claimed = 1;

    public static async Task<bool> RecordAttemptAsync(
        IServiceProvider services, long id, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var inquiry = scope.ServiceProvider.GetRequiredService<IInquiry>();
        var items = scope.ServiceProvider.GetRequiredService<WorkItemStore>();

        return await inquiry.ExecuteInTransactionAsync(async (_, token) =>
        {
            // The row stays locked until the transaction ends, so no other
            // writer can change Attempts between this read and the update.
            var item = await items.GetForUpdateAsync(id, token);
            if (item is null)
            {
                return false;
            }

            item.Attempts++;
            return await items.UpdateAsync(item, token);
        }, cancellationToken: ct);
    }

    public static async Task<IReadOnlyList<long>> ClaimAvailableAsync(
        IServiceProvider services, int queueId, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var inquiry = scope.ServiceProvider.GetRequiredService<IInquiry>();
        var items = scope.ServiceProvider.GetRequiredService<WorkItemStore>();

        return await inquiry.ExecuteInTransactionAsync<IReadOnlyList<long>>(async (_, token) =>
        {
            // Rows that another transaction has locked are skipped, not waited for.
            var available = await items.AvailableAsync(queueId, Pending, token);
            foreach (var item in available)
            {
                item.State = Claimed;
                await items.UpdateAsync(item, token);
            }

            return available.Select(item => item.Id).ToList();
        }, cancellationToken: ct);
    }
}
