using Inquiry.FeatureCatalog.LockingRegression;
using Microsoft.Extensions.DependencyInjection;

namespace Inquiry.Tests.Shared;

/// <summary>
/// Live check that offset-paged locking selects run (#538) on PostgreSQL, MySQL, MariaDB and SQL Server.
/// Oracle rejects them at build time. Each lock mode runs in its own transaction.
/// </summary>
internal static class RowLockRegressionPagedScenarios
{
    internal static async Task PagedLocksRunAsync(IServiceProvider services)
    {
        await RowLockRegressionScenarios.SeedAsync(services, RowLockRegressionScenarios.ShapeRows());
        await using var scope = services.CreateAsyncScope();
        var inquiry = scope.ServiceProvider.GetRequiredService<IInquiry>();
        var store = scope.ServiceProvider.GetRequiredService<LockProbePagedStore>();

        Func<int, int, Task<IReadOnlyList<LockProbeSummary>>>[] pages =
        [
            (offset, limit) => store.PageForUpdateAsync(offset, limit),
            (offset, limit) => store.PageForUpdateNoWaitAsync(offset, limit),
            (offset, limit) => store.PageForUpdateSkipLockedAsync(offset, limit),
            (offset, limit) => store.PageForShareAsync(offset, limit),
        ];
        foreach (var page in pages)
        {
            var rows = await inquiry.ExecuteInTransactionAsync(_ => page(1, 2));
            Assert.Equal(new long[] { 2, 3 }, rows.Select(static r => r.Id));
        }
    }
}
