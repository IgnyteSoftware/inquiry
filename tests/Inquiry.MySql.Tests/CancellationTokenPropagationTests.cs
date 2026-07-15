using Inquiry.Connections;
using Inquiry.MySql.Tests.Fixtures;
using Xunit;

namespace Inquiry.MySql.Tests;

[Collection(MySqlCollection.Name)]
public sealed class CancellationTokenPropagationTests
{
    private readonly MySqlContainerFixture _fixture;

    public CancellationTokenPropagationTests(MySqlContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task DirectMySqlConnectorCancellationStopsInFlightCommandPromptly()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateAsync(_fixture.AdminConnectionString, "cancel");
        var factory = harness.GetRequiredService<IInquiryConnectionFactory>();

        var connection = await factory.OpenConnectionAsync();
        var command = connection.CreateCommand();
        command.CommandText = "SELECT SLEEP(30)";

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(500));

        var ownershipTransferred = false;
        try
        {
            var execution = command.ExecuteScalarAsync(cts.Token);
            var completed = await Task.WhenAny(execution, Task.Delay(TimeSpan.FromSeconds(10)));
            if (completed != execution)
            {
                cts.Cancel();
                ownershipTransferred = true;
                var cleanup = Task.Run(async () =>
                {
                    try
                    {
                        command.Cancel();
                    }
                    finally
                    {
                        try
                        {
                            await command.DisposeAsync();
                        }
                        finally
                        {
                            await connection.DisposeAsync();
                        }
                    }
                });

                var cleanupGuard = Task.Delay(TimeSpan.FromSeconds(1));
                var cleanupObserved = await Task.WhenAny(cleanup, cleanupGuard);
                var cleanupFailure = cleanupObserved == cleanup
                    ? await Record.ExceptionAsync(() => cleanup)
                    : null;
                if (cleanupObserved != cleanup)
                {
                    ObserveLateFault(cleanup);
                }

                object? terminalResult = null;
                Exception? terminalOutcome = null;
                var executionObserved = await Task.WhenAny(execution, cleanupGuard);
                if (executionObserved == execution)
                {
                    terminalOutcome = await Record.ExceptionAsync(async () =>
                    {
                        terminalResult = await execution;
                    });
                }
                else
                {
                    ObserveLateFault(execution);
                }

                Assert.Fail(
                    "Direct MySqlConnector execution did not observe cancellation within 10 seconds. " +
                    $"Cleanup failure: {cleanupFailure?.ToString() ?? (cleanupObserved == cleanup ? "none" : "cleanup still running")}. " +
                    $"Terminal outcome after cleanup: {terminalOutcome?.ToString() ?? (executionObserved == execution ? $"result {terminalResult ?? "<null>"}" : "still running")}");
            }

            object? result = null;
            var outcome = await Record.ExceptionAsync(async () =>
            {
                result = await execution;
            });

            Assert.True(cts.IsCancellationRequested);
            switch (outcome)
            {
                case OperationCanceledException exception when exception.CancellationToken == cts.Token:
                    break;
                case null when IsScalarInterruptionResult(result):
                    break;
                case null:
                    Assert.Fail($"Unexpected MySqlConnector cancellation result: {result ?? "<null>"}.");
                    break;
                default:
                    Assert.Fail($"Unexpected MySqlConnector cancellation outcome: {outcome}");
                    break;
            }
        }
        finally
        {
            if (!ownershipTransferred)
            {
                try
                {
                    await command.DisposeAsync();
                }
                finally
                {
                    await connection.DisposeAsync();
                }
            }
        }
    }

    private static bool IsScalarInterruptionResult(object? result) => result switch
    {
        byte value => value == 1,
        sbyte value => value == 1,
        short value => value == 1,
        ushort value => value == 1,
        int value => value == 1,
        uint value => value == 1,
        long value => value == 1,
        ulong value => value == 1,
        decimal value => value == 1,
        _ => false,
    };

    private static void ObserveLateFault(Task task)
        => _ = task.ContinueWith(
            static completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
}
