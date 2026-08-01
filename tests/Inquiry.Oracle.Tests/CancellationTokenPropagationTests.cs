using Inquiry.Northwind.Models;
using Inquiry.Northwind.Stores;
using Inquiry.Oracle.Tests.Fixtures;
using Oracle.ManagedDataAccess.Client;

namespace Inquiry.Oracle.Tests;

[Collection(OracleCollection.Name)]
public sealed class CancellationTokenPropagationTests
{
    private readonly OracleContainerFixture _fixture;

    public CancellationTokenPropagationTests(OracleContainerFixture fixture) => _fixture = fixture;

    private static CancellationToken PreCancelled => new(canceled: true);

    [SkippableFact]
    public async Task GeneratedSelectAll_PreCancelled_Throws()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "cancelSelect");
        var store = harness.GetRequiredService<CustomerStore>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => store.SelectAllAsync(PreCancelled));
    }

    [SkippableFact]
    public async Task GeneratedInsert_PreCancelled_Throws()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "cancelInsert");
        var store = harness.GetRequiredService<CustomerStore>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => store.InsertAsync(new Customer { CustomerID = "CANC1", CompanyName = "Cancelled" }, PreCancelled));
    }

    [SkippableFact]
    public async Task IInquiry_ExecuteScalarAsync_PreCancelled_Throws()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "cancelScalar");
        var inquiry = harness.GetRequiredService<IInquiry>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => inquiry.ExecuteScalarAsync<long>($"SELECT COUNT(*) FROM \"Customers\"", PreCancelled));
    }

    [SkippableFact]
    public async Task GeneratedStreaming_PreCancelled_ThrowsOnFirstMoveNext()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "cancelStream");
        var store = harness.GetRequiredService<OrderStore>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in store.SelectAllAsync(PreCancelled))
            {
            }
        });
    }

    [SkippableFact]
    public async Task IInquiry_BeginTransactionAsync_PreCancelled_Throws()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "cancelTx");
        var inquiry = harness.GetRequiredService<IInquiry>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await using var tx = await inquiry.BeginTransactionAsync(cancellationToken: PreCancelled);
        });
    }

    [SkippableFact]
    public async Task InquiryPipelineCancellationStopsInFlightOperationPromptly()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        OracleTestHarness? harness = await OracleTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString,
            """
            CREATE TABLE TCancelProbe (Id NUMBER(10) PRIMARY KEY);
            INSERT INTO TCancelProbe (Id) SELECT LEVEL FROM dual CONNECT BY LEVEL <= 10000
            """,
            "cancel");

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        try
        {
            var inquiry = harness.GetRequiredService<IInquiry>();
            var execution = inquiry.ExecuteScalarAsync<decimal>(
                $"SELECT SUM(a.Id * b.Id * c.Id) FROM TCancelProbe a CROSS JOIN TCancelProbe b CROSS JOIN TCancelProbe c",
                cts.Token);

            var completed = await Task.WhenAny(execution, Task.Delay(TimeSpan.FromSeconds(10)));
            if (completed != execution)
            {
                cts.Cancel();
                var cleanup = ObserveAndDisposeAsync(execution, harness);
                harness = null; // The cleanup task is now the sole owner.

                var cleanupCompleted = await Task.WhenAny(cleanup, Task.Delay(TimeSpan.FromSeconds(15)));
                if (cleanupCompleted != cleanup)
                {
                    ObserveLateFault(cleanup);
                    Assert.Fail("Inquiry's Oracle command ignored cancellation and sole-owner cleanup exceeded its 15-second bound.");
                }

                await cleanup;
                Assert.Fail("Inquiry's Oracle command did not observe cancellation within the independent 10-second watchdog.");
            }

            var outcome = await Record.ExceptionAsync(() => execution);
            Assert.True(cts.IsCancellationRequested);
            Assert.True(
                outcome is OperationCanceledException canceled && canceled.CancellationToken == cts.Token
                || outcome is OracleException oracle && oracle.Number == 1013,
                $"Expected matching cancellation or ORA-01013, got {outcome?.GetType().Name ?? "successful completion"}: {outcome?.Message}");
        }
        finally
        {
            if (harness is not null)
            {
                await harness.DisposeAsync();
            }
        }
    }

    private static async Task ObserveAndDisposeAsync(Task execution, OracleTestHarness harness)
    {
        try
        {
            var executionCompleted = await Task.WhenAny(execution, Task.Delay(TimeSpan.FromSeconds(2)));
            if (executionCompleted == execution)
            {
                try
                {
                    await execution;
                }
                catch
                {
                    // The watchdog assertion reports the provider outcome; cleanup only observes it.
                }
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
