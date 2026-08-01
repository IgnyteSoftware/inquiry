using Inquiry.Pipeline;

namespace Inquiry.Tests;

/// <summary>
/// The success-path cancellation contract (<see cref="InquiryCancellation.AwaitEnforcingCallerToken{T}"/>):
/// some driver/server pairs report normal completion for a statement that cancellation actually cut short
/// (MySQL's <c>SLEEP()</c> under <c>KILL QUERY</c>, SqlClient's attention-cancelled <c>WAITFOR</c>).
/// These pin the race semantics deterministically, which the live container tests cannot — the live
/// tests depend on which side of the race the driver happens to land on.
/// </summary>
public sealed class InquiryCancellationRaceTests
{
    [Fact]
    public async Task CancellationWhileProviderPendingThrowsWithCallerTokenEvenIfProviderReportsSuccess()
    {
        // The lying-success shape: the token fires while the provider task is in flight, and the
        // provider then completes NORMALLY (the killed statement returned success).
        using var cts = new CancellationTokenSource();
        var provider = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        var pending = InquiryCancellation.AwaitEnforcingCallerToken(provider.Task, cts.Token);

        cts.Cancel();
        provider.SetResult(1);

        var exception = await Assert.ThrowsAsync<OperationCanceledException>(() => pending);
        Assert.Equal(cts.Token, exception.CancellationToken);
    }

    [Fact]
    public async Task CompletionThatBeatsTheTokenIsTrustedEvenWhenTheTokenFiresBeforeTheAwaitResumes()
    {
        // The shape a bare post-await ThrowIfCancellationRequested would get wrong: the provider
        // completed first, the token fired later, and the continuation observes both. The completed
        // (possibly committed) result must win — misreporting it as cancelled invites a caller retry
        // of work that already happened.
        using var cts = new CancellationTokenSource();
        var provider = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        provider.SetResult(42);
        cts.Cancel();

        var result = await InquiryCancellation.AwaitEnforcingCallerToken(provider.Task, cts.Token);

        Assert.Equal(42, result);
    }

    [Fact]
    public async Task ProviderThrownCancellationPassesThroughUnchanged()
    {
        // Drivers that DO throw (Npgsql, MySqlConnector on an errored kill) keep their exception; the
        // pipeline's existing catch normalizes foreign tokens, so the helper must not intercept it.
        using var cts = new CancellationTokenSource();
        var provider = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var driverException = new OperationCanceledException("driver", cts.Token);

        var pending = InquiryCancellation.AwaitEnforcingCallerToken(provider.Task, cts.Token);
        cts.Cancel();
        provider.SetException(driverException);

        var exception = await Assert.ThrowsAsync<OperationCanceledException>(() => pending);
        Assert.Same(driverException, exception);
    }

    [Fact]
    public async Task NativeDriverErrorAfterInFlightCancellationNormalizesToCallerTokenWithInnerPreserved()
    {
        // SqlClient's other face of the same race: an attention-cancelled command surfaces its native
        // "severe error … Operation cancelled by user" SqlException instead of an OCE. The caller
        // cancelled mid-flight, so the contract owes an OCE carrying their token; the driver exception
        // must ride along as the inner exception for diagnosability.
        using var cts = new CancellationTokenSource();
        var provider = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var driverException = new InvalidOperationException("A severe error occurred on the current command.");

        var pending = InquiryCancellation.AwaitEnforcingCallerToken(provider.Task, cts.Token);
        cts.Cancel();
        provider.SetException(driverException);

        var exception = await Assert.ThrowsAsync<OperationCanceledException>(() => pending);
        Assert.Equal(cts.Token, exception.CancellationToken);
        Assert.Same(driverException, exception.InnerException);
    }

    [Fact]
    public async Task DriverErrorWithoutCancellationPassesThroughUnchanged()
    {
        // A genuine failure with no cancellation in play must never be re-labelled as cancelled.
        using var cts = new CancellationTokenSource();
        var provider = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var driverException = new InvalidOperationException("network failure");
        provider.SetException(driverException);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => InquiryCancellation.AwaitEnforcingCallerToken(provider.Task, cts.Token));
        Assert.Same(driverException, exception);
    }

    [Fact]
    public async Task NonCancellableTokenIsAPlainPassThrough()
    {
        var provider = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        provider.SetResult(7);

        var result = await InquiryCancellation.AwaitEnforcingCallerToken(provider.Task, CancellationToken.None);

        Assert.Equal(7, result);
    }

    [Fact]
    public async Task UncancelledTokenReturnsTheProviderResult()
    {
        using var cts = new CancellationTokenSource();
        var provider = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        var pending = InquiryCancellation.AwaitEnforcingCallerToken(provider.Task, cts.Token);
        provider.SetResult(9);

        Assert.Equal(9, await pending);
    }
}
