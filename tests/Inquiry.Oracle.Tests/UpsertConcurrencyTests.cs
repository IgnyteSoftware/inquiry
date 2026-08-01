using Inquiry.Northwind.Models;
using Inquiry.Northwind.Stores;
using Inquiry.Oracle.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Oracle.ManagedDataAccess.Client;

namespace Inquiry.Oracle.Tests;

/// <summary>
/// Pins Oracle's caller-owned retry contract for concurrent client-key MERGE operations. Inquiry does
/// not retry a losing MERGE internally because replay safety belongs to the application transaction.
/// </summary>
[Collection(OracleCollection.Name)]
public sealed class UpsertConcurrencyTests
{
    private readonly OracleContainerFixture _fixture;

    public UpsertConcurrencyTests(OracleContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task LosingClientKeyMergeReportsUniqueConstraintAndCallerRetrySucceeds()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "upsert_race");
        await using var winnerScope = harness.Services.CreateAsyncScope();
        await using var loserScope = harness.Services.CreateAsyncScope();

        var winnerInquiry = winnerScope.ServiceProvider.GetRequiredService<IInquiry>();
        var winnerStore = winnerScope.ServiceProvider.GetRequiredService<CustomerStore>();
        var loserInquiry = loserScope.ServiceProvider.GetRequiredService<IInquiry>();
        var loserStore = loserScope.ServiceProvider.GetRequiredService<CustomerStore>();
        var winner = new Customer { CustomerID = "CONC1", CompanyName = "Winner", Country = "USA" };
        var loser = new Customer { CustomerID = "CONC1", CompanyName = "Retried", Country = "Canada" };
        var attempts = 0;

        await using var winnerTransaction = await winnerInquiry.BeginTransactionAsync();
        await winnerStore.UpsertAsync(winner);

        await using var loserTransaction = await loserInquiry.BeginTransactionAsync();
        attempts++;
        var firstLoserAttempt = loserStore.UpsertAsync(loser);
        await WaitUntilBlockedAsync(firstLoserAttempt, harness.SchemaUser);

        // The loser is now waiting on the winner's uncommitted key. Commit before awaiting it so the
        // provider deterministically resolves the race as ORA-00001 instead of a timing-dependent result.
        await winnerTransaction.CommitAsync();
        var conflict = await Assert.ThrowsAsync<OracleException>(() => firstLoserAttempt);
        Assert.Equal(1, conflict.Number);
        await loserTransaction.RollbackAsync();

        await using (var retryScope = harness.Services.CreateAsyncScope())
        {
            var retryInquiry = retryScope.ServiceProvider.GetRequiredService<IInquiry>();
            var retryStore = retryScope.ServiceProvider.GetRequiredService<CustomerStore>();
            await using var retryTransaction = await retryInquiry.BeginTransactionAsync();
            attempts++;
            await retryStore.UpsertAsync(loser);
            await retryTransaction.CommitAsync();
        }

        Assert.Equal(2, attempts);
        await using var verificationScope = harness.Services.CreateAsyncScope();
        var verificationInquiry = verificationScope.ServiceProvider.GetRequiredService<IInquiry>();
        var verificationStore = verificationScope.ServiceProvider.GetRequiredService<CustomerStore>();
        var count = await verificationInquiry.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM Customers");
        var stored = await verificationStore.SelectByKeyAsync("CONC1");

        Assert.Equal(1, count);
        Assert.NotNull(stored);
        Assert.Equal("Retried", stored.CompanyName);
        Assert.Equal("Canada", stored.Country);
    }

    private async Task WaitUntilBlockedAsync(Task loserAttempt, string schemaUser)
    {
        using var watchdog = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var admin = new OracleConnection(_fixture.AdminConnectionString);
        await admin.OpenAsync(watchdog.Token);

        while (true)
        {
            if (loserAttempt.IsCompleted)
            {
                var earlyOutcome = await Record.ExceptionAsync(() => loserAttempt);
                Assert.Fail($"The losing MERGE completed before it blocked on the winner. Outcome: {earlyOutcome?.ToString() ?? "success"}");
            }

            await using var command = admin.CreateCommand();
            command.BindByName = true;
            command.CommandText = """
                SELECT COUNT(*)
                FROM V$SESSION
                WHERE USERNAME = :username
                  AND BLOCKING_SESSION IS NOT NULL
                """;
            command.Parameters.Add("username", OracleDbType.Varchar2).Value = schemaUser;
            if (Convert.ToInt32(await command.ExecuteScalarAsync(watchdog.Token)) > 0)
            {
                return;
            }

            await Task.Delay(50, watchdog.Token);
        }
    }
}
