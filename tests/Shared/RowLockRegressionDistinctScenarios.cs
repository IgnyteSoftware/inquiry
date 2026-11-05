using Inquiry.FeatureCatalog.LockingRegression;
using Microsoft.Extensions.DependencyInjection;

namespace Inquiry.Tests.Shared;

/// <summary>
/// Live check that DISTINCT locking selects, with and without offset paging, run (#538) on MySQL,
/// MariaDB and SQL Server. PostgreSQL and Oracle reject them at build time. Each lock mode runs in its
/// own transaction.
/// </summary>
internal static class RowLockRegressionDistinctScenarios
{
    private static readonly int[] _distinctStates = [1, 2, 3];

    internal static async Task DistinctLocksRunAsync(IServiceProvider services)
    {
        await RowLockRegressionScenarios.SeedAsync(services, RowLockRegressionScenarios.ShapeRows());
        await using var scope = services.CreateAsyncScope();
        var inquiry = scope.ServiceProvider.GetRequiredService<IInquiry>();
        var store = scope.ServiceProvider.GetRequiredService<LockProbeDistinctStore>();

        Func<Task<IReadOnlyList<LockProbeState>>>[] reads =
        [
            () => store.StatesForUpdateAsync(),
            () => store.StatesForUpdateNoWaitAsync(),
            () => store.StatesForUpdateSkipLockedAsync(),
            () => store.StatesForShareAsync(),
        ];
        foreach (var read in reads)
        {
            var states = await inquiry.ExecuteInTransactionAsync(_ => read());
            Assert.Equal(_distinctStates, states.Select(static s => s.State).Order());
        }

        Func<int, int, Task<IReadOnlyList<LockProbeState>>>[] pages =
        [
            (offset, limit) => store.StatesPageForUpdateAsync(offset, limit),
            (offset, limit) => store.StatesPageForUpdateNoWaitAsync(offset, limit),
            (offset, limit) => store.StatesPageForUpdateSkipLockedAsync(offset, limit),
            (offset, limit) => store.StatesPageForShareAsync(offset, limit),
        ];
        foreach (var page in pages)
        {
            var states = await inquiry.ExecuteInTransactionAsync(_ => page(1, 1));
            Assert.Equal(2, Assert.Single(states).State);
        }
    }
}
