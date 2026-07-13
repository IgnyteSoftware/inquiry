using Inquiry.Connections;
using Inquiry.SqlServer.Tests.Fixtures;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Inquiry.SqlServer.Tests;

[Collection(SqlServerCollection.Name)]
public sealed class CancellationTokenPropagationTests
{
    private readonly SqlServerContainerFixture _fixture;

    public CancellationTokenPropagationTests(SqlServerContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task DirectSqlClientCancellationStopsInFlightCommandPromptly()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateAsync(_fixture.AdminConnectionString, "cancel");
        var factory = harness.GetRequiredService<IInquiryConnectionFactory>();

        await using var connection = await factory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "WAITFOR DELAY '00:00:30'";

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(500));

        var execution = command.ExecuteNonQueryAsync(cts.Token);
        var completed = await Task.WhenAny(execution, Task.Delay(TimeSpan.FromSeconds(10)));
        if (completed != execution)
        {
            cts.Cancel();
            Exception? cleanupFailure = null;
            try
            {
                command.Cancel();
                await connection.CloseAsync();
            }
            catch (Exception exception)
            {
                cleanupFailure = exception;
            }

            Assert.Fail(
                "Direct SqlClient execution did not observe cancellation within 10 seconds. " +
                $"Cleanup failure: {cleanupFailure?.ToString() ?? "none"}");
        }

        var outcome = await Record.ExceptionAsync(() => execution);

        Assert.True(cts.IsCancellationRequested);
        switch (outcome)
        {
            case OperationCanceledException:
                break;
            case SqlException exception when IsSqlClientCancellation(exception):
                break;
            case null:
                Assert.Fail("The 30-second SqlClient command completed instead of observing cancellation.");
                break;
            default:
                Assert.Fail($"Unexpected cancellation outcome: {outcome}");
                break;
        }
    }

    private static bool IsSqlClientCancellation(SqlException exception)
    {
        // Microsoft.Data.SqlClient 7.0.1 reports command cancellation as two SqlErrors: the discarded-results
        // warning and the cancellation itself. Across net8/net9/net10 both use this structured tuple, so the
        // individual provider cancellation phrase is also required to distinguish the second error.
        if (exception.Errors.Count != 2)
        {
            return false;
        }

        var hasCancellationError = false;
        foreach (SqlError error in exception.Errors)
        {
            if (error.Number != 0 || error.Class != 11 || error.State != 0)
            {
                return false;
            }

            if (error.Message.Contains("Operation cancelled by user.", StringComparison.Ordinal))
            {
                hasCancellationError = true;
            }
        }

        return hasCancellationError;
    }
}
