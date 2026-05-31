using System.Data.Common;
using Inquiry.Connections;

namespace Inquiry.Tests;

public sealed class RetryingConnectionOpenerTests
{
    private sealed class PredicateDetector : ITransientErrorDetector
    {
        private readonly Func<Exception, bool> _predicate;

        public PredicateDetector(Func<Exception, bool> predicate) => _predicate = predicate;

        public bool IsTransient(Exception exception) => _predicate(exception);
    }

    private sealed class TransientException : Exception
    {
    }

    private sealed class TerminalException : Exception
    {
    }

    private sealed class FakeConnection : DbConnection
    {
        public override string ConnectionString { get; set; } = string.Empty;
        public override string Database => string.Empty;
        public override string DataSource => string.Empty;
        public override string ServerVersion => string.Empty;
        public override System.Data.ConnectionState State => System.Data.ConnectionState.Open;
        public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        public override void Close() { }
        public override void Open() { }
        protected override DbCommand CreateDbCommand() => throw new NotSupportedException();
        protected override DbTransaction BeginDbTransaction(System.Data.IsolationLevel il) => throw new NotSupportedException();
    }

    private static RetryingConnectionOpener TransientOpener(int maxAttempts, out List<TimeSpan> delays)
    {
        var recorded = new List<TimeSpan>();
        delays = recorded;
        return new RetryingConnectionOpener(
            new PredicateDetector(ex => ex is TransientException),
            maxAttempts,
            TimeSpan.FromMilliseconds(100),
            delay: (d, _) =>
            {
                recorded.Add(d);
                return Task.CompletedTask;
            },
            jitter: () => 0.0);
    }

    [Fact]
    public async Task RetriesTransientFailuresThenSucceeds()
    {
        var opener = TransientOpener(maxAttempts: 4, out var delays);
        var attempts = 0;
        var connection = new FakeConnection();

        var result = await opener.OpenAsync(_ =>
        {
            attempts++;
            if (attempts < 3)
            {
                throw new TransientException();
            }

            return new ValueTask<DbConnection>(connection);
        });

        Assert.Same(connection, result);
        Assert.Equal(3, attempts);
        Assert.Equal(2, delays.Count); // delayed before retry 2 and retry 3
    }

    [Fact]
    public async Task NonTransientFailurePropagatesImmediately()
    {
        var opener = TransientOpener(maxAttempts: 5, out var delays);
        var attempts = 0;

        await Assert.ThrowsAsync<TerminalException>(() => opener.OpenAsync(_ =>
        {
            attempts++;
            throw new TerminalException();
        }).AsTask());

        Assert.Equal(1, attempts);
        Assert.Empty(delays);
    }

    [Fact]
    public async Task AttemptCapIsHonored()
    {
        var opener = TransientOpener(maxAttempts: 3, out var delays);
        var attempts = 0;

        await Assert.ThrowsAsync<TransientException>(() => opener.OpenAsync(_ =>
        {
            attempts++;
            throw new TransientException();
        }).AsTask());

        Assert.Equal(3, attempts);
        Assert.Equal(2, delays.Count); // delays only between attempts, not after the last
    }

    [Fact]
    public async Task DelayGrowsExponentiallyWithDeterministicJitter()
    {
        var opener = TransientOpener(maxAttempts: 4, out var delays);

        await Assert.ThrowsAsync<TransientException>(() => opener.OpenAsync(_ => throw new TransientException()).AsTask());

        // baseDelay 100ms, jitter 0 => factor 2^(n-1): 100ms, 200ms, 400ms.
        Assert.Equal(new[]
        {
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMilliseconds(200),
            TimeSpan.FromMilliseconds(400),
        }, delays);
    }

    [Fact]
    public void RejectsInvalidMaxAttempts()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RetryingConnectionOpener(new PredicateDetector(_ => true), maxAttempts: 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task CancellationIsNotRetriedAndPropagates()
    {
        var opener = TransientOpener(maxAttempts: 5, out var delays);
        var attempts = 0;
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => opener.OpenAsync(ct =>
        {
            attempts++;
            ct.ThrowIfCancellationRequested();
            return new ValueTask<DbConnection>(new FakeConnection());
        }, cts.Token).AsTask());

        Assert.Equal(1, attempts); // a cancelled token is not transient — no retry
        Assert.Empty(delays);
    }
}
