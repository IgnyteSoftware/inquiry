using Inquiry.Connections;
using Microsoft.Data.Sqlite;
using System.Data.Common;

namespace Inquiry.Tests;

public sealed class FailoverConnectionOpenerTests
{
    private const string Primary = "primary";
    private const string Failover = "failover";

    [Fact]
    public async Task UsesPrimaryWhenItOpens()
    {
        var opened = new List<string>();

        await using var connection = await FailoverConnectionOpener.OpenAsync(
            (cs, _) =>
            {
                opened.Add(cs);
                return new ValueTask<DbConnection>(new SqliteConnection());
            },
            Primary,
            Failover,
            retryingOpener: null,
            CancellationToken.None);

        Assert.Equal(new[] { Primary }, opened);
    }

    [Fact]
    public async Task FallsBackToFailoverWhenPrimaryFails()
    {
        var opened = new List<string>();

        await using var connection = await FailoverConnectionOpener.OpenAsync(
            (cs, _) =>
            {
                opened.Add(cs);
                return cs == Primary
                    ? ValueTask.FromException<DbConnection>(new InvalidOperationException("primary down"))
                    : new ValueTask<DbConnection>(new SqliteConnection());
            },
            Primary,
            Failover,
            retryingOpener: null,
            CancellationToken.None);

        Assert.Equal(new[] { Primary, Failover }, opened);
    }

    [Fact]
    public async Task ThrowsAggregateWhenBothServersFail()
    {
        var primaryFault = new InvalidOperationException("primary down");
        var failoverFault = new InvalidOperationException("failover down");

        var exception = await Assert.ThrowsAsync<AggregateException>(() => FailoverConnectionOpener.OpenAsync(
            (cs, _) => ValueTask.FromException<DbConnection>(cs == Primary ? primaryFault : failoverFault),
            Primary,
            Failover,
            retryingOpener: null,
            CancellationToken.None).AsTask());

        Assert.Equal(new Exception[] { primaryFault, failoverFault }, exception.InnerExceptions);
    }

    [Fact]
    public async Task DoesNotFailOverOnCancellation()
    {
        var opened = new List<string>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => FailoverConnectionOpener.OpenAsync(
            (cs, _) =>
            {
                opened.Add(cs);
                return ValueTask.FromException<DbConnection>(new OperationCanceledException());
            },
            Primary,
            Failover,
            retryingOpener: null,
            CancellationToken.None).AsTask());

        Assert.Equal(new[] { Primary }, opened);
    }

    [Fact]
    public async Task AppliesRetryPolicyToEachServer()
    {
        var opened = new List<string>();
        var retrying = new RetryingConnectionOpener(
            new AlwaysTransientDetector(),
            maxAttempts: 2,
            baseDelay: TimeSpan.Zero,
            delay: (_, _) => Task.CompletedTask,
            jitter: () => 0.0);

        await using var connection = await FailoverConnectionOpener.OpenAsync(
            (cs, _) =>
            {
                opened.Add(cs);
                return cs == Primary
                    ? ValueTask.FromException<DbConnection>(new InvalidOperationException("primary down"))
                    : new ValueTask<DbConnection>(new SqliteConnection());
            },
            Primary,
            Failover,
            retrying,
            CancellationToken.None);

        // Two retried attempts against the primary, then the failover succeeds on its first try.
        Assert.Equal(new[] { Primary, Primary, Failover }, opened);
    }

    private sealed class AlwaysTransientDetector : ITransientErrorDetector
    {
        public bool IsTransient(Exception exception) => true;
    }
}
