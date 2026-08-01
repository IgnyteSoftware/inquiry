using Inquiry.Northwind.Models;
using Inquiry.Northwind.Stores;
using Inquiry.MySql.Tests.Fixtures;

namespace Inquiry.MySql.Tests;

[Collection(MySqlCollection.Name)]
public sealed class CancellationTokenPropagationTests
{
    private readonly MySqlContainerFixture _fixture;

    public CancellationTokenPropagationTests(MySqlContainerFixture fixture) => _fixture = fixture;

    private static CancellationToken PreCancelled => new(canceled: true);

    [SkippableFact]
    public async Task InquiryPipelineCancellationStopsInFlightOperationPromptly()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        MySqlTestHarness? harness = await MySqlTestHarness.CreateAsync(_fixture.AdminConnectionString, "cancel");

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        try
        {
            var inquiry = harness.GetRequiredService<IInquiry>();
            var execution = inquiry.ExecuteScalarAsync<long>($"SELECT SLEEP(30)", cts.Token);

            var completed = await Task.WhenAny(execution, Task.Delay(TimeSpan.FromSeconds(10)));
            if (completed != execution)
            {
                cts.Cancel();
                var cleanup = ObserveAndDisposeAsync(execution, harness);
                harness = null;

                var cleanupCompleted = await Task.WhenAny(cleanup, Task.Delay(TimeSpan.FromSeconds(15)));
                if (cleanupCompleted != cleanup)
                {
                    ObserveLateFault(cleanup);
                    Assert.Fail("Inquiry pipeline did not observe cancellation within 15 seconds.");
                }

                await cleanup;
                Assert.Fail("Inquiry pipeline did not observe cancellation within the 10-second watchdog.");
            }

            var outcome = await Record.ExceptionAsync(() => execution);
            Assert.True(cts.IsCancellationRequested);
            Assert.True(
                outcome is OperationCanceledException canceled && canceled.CancellationToken == cts.Token,
                $"Expected OperationCanceledException with caller token, got {outcome?.GetType().Name ?? "successful completion"}: {outcome?.Message}");
        }
        finally
        {
            if (harness is not null)
                await harness.DisposeAsync();
        }
    }

    [SkippableFact]
    public async Task GeneratedSelectAll_PreCancelled_Throws()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateAsync(_fixture.AdminConnectionString, "cancelSelect");
        var store = harness.GetRequiredService<CustomerStore>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => store.SelectAllAsync(PreCancelled));
    }

    [SkippableFact]
    public async Task GeneratedInsert_PreCancelled_Throws()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateAsync(_fixture.AdminConnectionString, "cancelInsert");
        var store = harness.GetRequiredService<CustomerStore>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => store.InsertAsync(new Customer { CustomerID = "CANC1", CompanyName = "Cancelled" }, PreCancelled));
    }

    [SkippableFact]
    public async Task GeneratedStreaming_PreCancelled_ThrowsOnFirstMoveNext()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateAsync(_fixture.AdminConnectionString, "cancelStream");
        var store = harness.GetRequiredService<OrderStore>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in store.SelectAllAsync(PreCancelled))
            {
            }
        });
    }

    [SkippableFact]
    public async Task IInquiry_ExecuteScalarAsync_PreCancelled_Throws()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateAsync(_fixture.AdminConnectionString, "cancelScalar");
        var inquiry = harness.GetRequiredService<IInquiry>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => inquiry.ExecuteScalarAsync<long>($"SELECT COUNT(*) FROM `Customers`", PreCancelled));
    }

    [SkippableFact]
    public async Task IInquiry_BeginTransactionAsync_PreCancelled_Throws()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateAsync(_fixture.AdminConnectionString, "cancelTx");
        var inquiry = harness.GetRequiredService<IInquiry>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await using var tx = await inquiry.BeginTransactionAsync(cancellationToken: PreCancelled);
        });
    }

    private static async Task ObserveAndDisposeAsync(Task execution, MySqlTestHarness harness)
    {
        try
        {
            var completed = await Task.WhenAny(execution, Task.Delay(TimeSpan.FromSeconds(2)));
            if (completed == execution)
            {
                try { await execution; }
                catch { }
            }
            else
            {
                ObserveLateFault(execution);
            }
        }
        finally
        {
            await harness.DisposeAsync();
        }
    }

    private static void ObserveLateFault(Task task)
        => _ = task.ContinueWith(
            static completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
}
