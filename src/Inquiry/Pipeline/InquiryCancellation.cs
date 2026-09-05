namespace Inquiry.Pipeline;

/// <summary>Normalizes provider cancellation exceptions at the pipeline boundary.</summary>
internal static class InquiryCancellation
{
    internal static bool RequiresCallerToken(
        OperationCanceledException exception,
        CancellationToken callerToken)
        => callerToken.IsCancellationRequested && exception.CancellationToken != callerToken;

    internal static OperationCanceledException AssociateWithCallerToken(
        OperationCanceledException exception,
        CancellationToken callerToken)
        => new(exception.Message, exception, callerToken);

    internal static void ThrowIfRequiresCallerToken(
        Exception exception,
        CancellationToken callerToken)
    {
        if (exception is OperationCanceledException oce && RequiresCallerToken(oce, callerToken))
            throw AssociateWithCallerToken(oce, callerToken);
    }

    /// <summary>
    /// Awaits a provider execution task while enforcing the caller-token contract on the SUCCESS path.
    /// Some driver/server pairs report normal completion for a statement that cancellation actually cut
    /// short: MySQL's <c>SLEEP()</c> returns success when interrupted by <c>KILL QUERY</c>, and
    /// SqlClient's attention-based cancel can surface a completed task for an aborted <c>WAITFOR</c>.
    /// The pipeline's catch blocks never see those — there is no exception to normalize — so a caller
    /// that cancelled mid-flight would observe a successful result for work the server abandoned.
    /// </summary>
    /// <remarks>
    /// The enforcement is a race, not a post-await token check. A bare
    /// <c>ThrowIfCancellationRequested()</c> after the await would also fire when the token cancels
    /// AFTER the provider completed but before this continuation resumes, misreporting a genuinely
    /// committed operation (dangerous for DML a caller might then retry). Instead the token callback
    /// records cancellation only while the provider task is still pending: completion observed to have
    /// beaten the token is trusted; cancellation that beat completion surfaces as
    /// <see cref="OperationCanceledException"/> carrying the caller's token — the same indeterminate
    /// outcome ADO.NET drivers themselves report after an in-flight cancel. When the two land within
    /// the same visibility window (sub-microsecond), either resolution can win; both directions sit
    /// inside the indeterminacy the driver already imposes, so neither is a misreport.
    /// </remarks>
    internal static Task<T> AwaitEnforcingCallerToken<T>(Task<T> providerTask, CancellationToken callerToken)
        => !callerToken.CanBeCanceled
            ? providerTask
            : AwaitEnforcingCallerTokenCore(providerTask, callerToken);

    private static async Task<T> AwaitEnforcingCallerTokenCore<T>(Task<T> providerTask, CancellationToken callerToken)
    {
        // The re-await is on an already-settled task (the core either completed it or threw), so it
        // resolves synchronously to the result.
        await AwaitEnforcingCallerTokenCore((Task)providerTask, callerToken).ConfigureAwait(false);
        return await providerTask.ConfigureAwait(false);
    }

    /// <summary>
    /// Register-first variant for provider awaits with no downstream token check to backstop them
    /// (the batch transaction's CommitAsync). The task-taking overloads have a window between the
    /// provider call starting and the registration landing; for command executes that window is
    /// closed by the chunk reader's next MoveNext (batch) or is equivalent to the driver's own
    /// indeterminacy (single command), but a commit is the last provider call of its operation —
    /// a lying success in that window would be reported as a committed batch. Here the callback is
    /// registered BEFORE the provider call starts, so cancellation can never slip between them; a
    /// callback that fires before the task exists records cancellation-won by definition.
    /// </summary>
    internal static Task AwaitEnforcingCallerToken(Func<CancellationToken, Task> startProviderTask, CancellationToken callerToken)
        => !callerToken.CanBeCanceled
            ? startProviderTask(callerToken)
            : AwaitEnforcingCallerTokenCore(startProviderTask, callerToken);

    private static async Task AwaitEnforcingCallerTokenCore(Func<CancellationToken, Task> startProviderTask, CancellationToken callerToken)
    {
        var race = new CancellationRace();
        CancellationTokenRegistration registration = callerToken.UnsafeRegister(
            static state => ((CancellationRace)state!).RecordIfPending(), race);
        await using (registration.ConfigureAwait(false))
        {
            var providerTask = startProviderTask(callerToken);
            race.SetTask(providerTask);
            await AwaitRegisteredAsync(providerTask, race, callerToken).ConfigureAwait(false);
        }
    }

    /// <summary>Non-generic variant for result-less provider awaits already started by the caller.</summary>
    internal static Task AwaitEnforcingCallerToken(Task providerTask, CancellationToken callerToken)
        => !callerToken.CanBeCanceled
            ? providerTask
            : AwaitEnforcingCallerTokenCore(providerTask, callerToken);

    private static async Task AwaitEnforcingCallerTokenCore(Task providerTask, CancellationToken callerToken)
    {
        var race = new CancellationRace();
        race.SetTask(providerTask);
        CancellationTokenRegistration registration = callerToken.UnsafeRegister(
            static state => ((CancellationRace)state!).RecordIfPending(), race);
        await using (registration.ConfigureAwait(false))
        {
            await AwaitRegisteredAsync(providerTask, race, callerToken).ConfigureAwait(false);
        }
    }

    private static async Task AwaitRegisteredAsync(Task providerTask, CancellationRace race, CancellationToken callerToken)
    {
        try
        {
            await providerTask.ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException && race.CancelledWhilePending)
        {
            // The other face of the same race: instead of lying success, the driver surfaces its
            // NATIVE cancellation error (SqlClient's "severe error … Operation cancelled by user"
            // SqlException from an attention-cancelled command). The caller cancelled mid-flight, so
            // the contract owes them an OCE with their token; the driver exception rides along as
            // the inner exception. OperationCanceledExceptions are deliberately excluded — the
            // pipeline's existing catch normalizes foreign-token OCEs, and a caller-token OCE is
            // already correct.
            //
            // Deliberate breadth: a genuine failure (deadlock, network drop) that lands in the
            // narrow window after the token fired is also re-labelled OCE. Classifying "true"
            // driver cancellation errors would need per-provider error codes the core pipeline
            // does not know; the caller asked to stop either way, and the real failure stays
            // fully diagnosable as the inner exception.
            throw NormalizeNativeError(exception, callerToken);
        }

        if (race.CancelledWhilePending)
        {
            throw new OperationCanceledException(
                "The operation was canceled while the command was in flight; the provider reported completion after cancellation was requested, so the outcome on the server is indeterminate.",
                callerToken);
        }
    }

    /// <summary>
    /// Wraps a driver-native (non-OCE) failure observed after the caller's token fired into the
    /// <see cref="OperationCanceledException"/> the public contract owes, with the driver exception
    /// preserved as the inner exception. Shared by the single-command race helper above and the batch
    /// boundary catch — the batch path gates on the token being cancelled at catch time rather than a
    /// per-await race, because its per-item loop cannot afford a registration per execute and its
    /// success face is already enforced by the chunk reader's per-move token check.
    /// </summary>
    internal static OperationCanceledException NormalizeNativeError(Exception exception, CancellationToken callerToken)
        => new(
            "The operation was canceled while the command was in flight; the provider reported a native error after cancellation was requested.",
            exception,
            callerToken);

    private sealed class CancellationRace
    {
        private Task? _providerTask;
        private int _cancelledWhilePending;

        internal void SetTask(Task providerTask) => Volatile.Write(ref _providerTask, providerTask);

        internal bool CancelledWhilePending => Volatile.Read(ref _cancelledWhilePending) == 1;

        internal void RecordIfPending()
        {
            // The check-then-write pair is deliberately not atomic. If the task transitions to
            // completed between this check and the write, OCE still wins — the outcome was genuinely
            // concurrent and cancellation is the honest answer. The reverse interleaving exists too
            // (this callback preempted after the check while the continuation reads the flag first,
            // resolving toward success); both orderings live inside the same sub-microsecond window
            // where the driver's own outcome is already indeterminate. The case this guard exists
            // for — the token firing after the provider clearly settled — reads IsCompleted == true
            // and records nothing. A null task (register-first overload, token fired before the
            // provider call started) is pending by definition.
            var providerTask = Volatile.Read(ref _providerTask);
            if (providerTask is null || !providerTask.IsCompleted)
                Volatile.Write(ref _cancelledWhilePending, 1);
        }
    }
}
