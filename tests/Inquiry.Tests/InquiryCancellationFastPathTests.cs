using Inquiry.Pipeline;

namespace Inquiry.Tests;

/// <summary>
/// The cancelable-token execution path (#507). A provider task that already completed cannot have lost the
/// cancellation race, so it is returned as is. A pending task gets the same race enforcement as before.
/// </summary>
public sealed class InquiryCancellationFastPathTests
{
    [Fact]
    public void CompletedProviderTaskIsReturnedUnwrapped()
    {
        using var cts = new CancellationTokenSource();
        var provider = Task.FromResult(42);

        var result = InquiryCancellation.AwaitEnforcingCallerToken(provider, cts.Token);

        Assert.Same(provider, result);
    }

    [Fact]
    public void CompletedFaultedProviderTaskIsReturnedUnwrappedWhenTokenAlreadyCancelled()
    {
        // The driver error completed before the token fired, so it stays the driver's error.
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var provider = Task.FromException<int>(new InvalidOperationException("driver"));

        var result = InquiryCancellation.AwaitEnforcingCallerToken(provider, cts.Token);

        Assert.Same(provider, result);
    }

    [Fact]
    public void CompletedCanceledProviderTaskIsReturnedUnwrapped()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var provider = Task.FromCanceled<object?>(cts.Token);

        var result = InquiryCancellation.AwaitEnforcingCallerToken(provider, cts.Token);

        Assert.Same(provider, result);
    }

    [Fact]
    public async Task PendingCancellationThenSuccessThrowsWithCallerToken()
    {
        using var cts = new CancellationTokenSource();
        var provider = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        var pending = InquiryCancellation.AwaitExecutionAsync(provider.Task, cts.Token);
        await cts.CancelAsync();
        provider.SetResult(1);

        var exception = await Assert.ThrowsAsync<OperationCanceledException>(async () => await pending);
        Assert.Equal(cts.Token, exception.CancellationToken);
    }

    [Fact]
    public async Task PendingCancellationThenNativeErrorThrowsWithCallerTokenAndInnerError()
    {
        using var cts = new CancellationTokenSource();
        var provider = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var driverException = new InvalidOperationException("A severe error occurred on the current command.");

        var pending = InquiryCancellation.AwaitExecutionAsync(provider.Task, cts.Token);
        await cts.CancelAsync();
        provider.SetException(driverException);

        var exception = await Assert.ThrowsAsync<OperationCanceledException>(async () => await pending);
        Assert.Equal(cts.Token, exception.CancellationToken);
        Assert.Same(driverException, exception.InnerException);
    }

    [Fact]
    public async Task PendingProviderCancellationPassesThroughUnchanged()
    {
        using var cts = new CancellationTokenSource();
        var provider = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var driverException = new OperationCanceledException("driver");

        var pending = InquiryCancellation.AwaitExecutionAsync(provider.Task, cts.Token);
        await cts.CancelAsync();
        provider.SetException(driverException);

        var exception = await Assert.ThrowsAsync<OperationCanceledException>(async () => await pending);
        Assert.Same(driverException, exception);
    }

    [Fact]
    public async Task PendingErrorWithoutCancellationPassesThroughUnchanged()
    {
        using var cts = new CancellationTokenSource();
        var provider = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var driverException = new InvalidOperationException("network failure");

        var pending = InquiryCancellation.AwaitExecutionAsync(provider.Task, cts.Token);
        provider.SetException(driverException);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () => await pending);
        Assert.Same(driverException, exception);
    }

    [Fact]
    public async Task PendingTaskThatCompletesBeforeTheTokenFiresIsTrusted()
    {
        using var cts = new CancellationTokenSource();
        var provider = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        var pending = InquiryCancellation.AwaitExecutionAsync(provider.Task, cts.Token);
        provider.SetResult(42);
        await cts.CancelAsync();

        Assert.Equal(42, await pending);
    }

    [Fact]
    public async Task PendingTaskReturnsTheProviderResult()
    {
        using var cts = new CancellationTokenSource();
        var provider = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var value = new object();

        var pending = InquiryCancellation.AwaitExecutionAsync(provider.Task, cts.Token);
        provider.SetResult(value);

        Assert.Same(value, await pending);
    }

    [Fact]
    public async Task ReusedRaceCarriesNoStateFromACancelledCall()
    {
        // Completing inline keeps both calls on this thread, so the second call reuses the first's race.
        using var cancelled = new CancellationTokenSource();
        var first = new TaskCompletionSource<int>();
        var firstPending = InquiryCancellation.AwaitExecutionAsync(first.Task, cancelled.Token);
        await cancelled.CancelAsync();
        first.SetResult(1);
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await firstPending);

        using var live = new CancellationTokenSource();
        var second = new TaskCompletionSource<int>();
        var secondPending = InquiryCancellation.AwaitExecutionAsync(second.Task, live.Token);
        await cancelled.CancelAsync();
        second.SetResult(2);

        Assert.Equal(2, await secondPending);
    }

    [Fact]
    public async Task ConcurrentCallsWithDifferentTokensDoNotAffectEachOther()
    {
        using var cancelled = new CancellationTokenSource();
        using var live = new CancellationTokenSource();
        var cancelledProvider = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var liveProvider = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        var cancelledPending = InquiryCancellation.AwaitEnforcingCallerToken(cancelledProvider.Task, cancelled.Token);
        var livePending = InquiryCancellation.AwaitEnforcingCallerToken(liveProvider.Task, live.Token);
        await cancelled.CancelAsync();
        cancelledProvider.SetResult(1);
        liveProvider.SetResult(2);

        await Assert.ThrowsAsync<OperationCanceledException>(() => cancelledPending);
        Assert.Equal(2, await livePending);
    }
}
