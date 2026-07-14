using Inquiry.Northwind.Models;
using Inquiry.Northwind.Stores;
using Inquiry.PostgreSql.Tests.Fixtures;
using Inquiry.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Inquiry.PostgreSql.Tests;

[Collection(PostgreSqlCollection.Name)]
public sealed class InquirySandboxIntegrationTests
{
    private readonly PostgreSqlContainerFixture _fixture;

    public InquirySandboxIntegrationTests(PostgreSqlContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task ParallelRunsAgainstOneDatabaseAreIsolatedAndRolledBack()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await PostgreSqlTestHarness.CreateAsync(_fixture.AdminConnectionString, "Sandbox");
        var sandbox = new InquirySandbox(harness.Services);
        var bothInserted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var coordination = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        var ready = 0;

        async Task RunAsync(string ownId, string otherId)
        {
            try
            {
                await sandbox.RunAsync(async (context, token) =>
                {
                    var store = context.Services.GetRequiredService<CustomerStore>();
                    await store.InsertAsync(new Customer { CustomerID = ownId, CompanyName = ownId }, token);
                    if (Interlocked.Increment(ref ready) == 2) bothInserted.TrySetResult();
                    await bothInserted.Task.WaitAsync(TimeSpan.FromSeconds(15), token);

                    Assert.NotNull(await store.SelectByKeyAsync(ownId, token));
                    Assert.Null(await store.SelectByKeyAsync(otherId, token));
                }, coordination.Token);
            }
            catch (Exception exception)
            {
                bothInserted.TrySetException(exception);
                coordination.Cancel();
                throw;
            }
        }

        await Task.WhenAll(RunAsync("SBX01", "SBX02"), RunAsync("SBX02", "SBX01"));

        var store = harness.GetRequiredService<CustomerStore>();
        Assert.Null(await store.SelectByKeyAsync("SBX01"));
        Assert.Null(await store.SelectByKeyAsync("SBX02"));
    }
}
