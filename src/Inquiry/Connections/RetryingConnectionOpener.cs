using System.Data.Common;

namespace Inquiry.Connections;

/// <summary>
/// Opens a database connection with exponential-backoff-plus-jitter retry, retrying only faults
/// classified as transient by an <see cref="ITransientErrorDetector"/>. Used by provider
/// connection factories for cloud engines (Azure SQL, CockroachDB, Aurora) where transient
/// throttling / failover faults are expected at open time.
/// </summary>
/// <remarks>
/// Retries the open operation only. Statement and transaction retry are out of scope. Timing
/// dependencies (delay and jitter) are injectable so the retry policy can be tested
/// deterministically without sleeping or relying on a wall clock.
/// </remarks>
public sealed class RetryingConnectionOpener
{
    private readonly ITransientErrorDetector _detector;
    private readonly int _maxAttempts;
    private readonly TimeSpan _baseDelay;
    private readonly TimeSpan _maxDelay;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Func<double> _jitter;

    /// <summary>
    /// Initializes a new instance of the <see cref="RetryingConnectionOpener"/> class.
    /// </summary>
    /// <param name="detector">Classifies open-time exceptions as transient or terminal.</param>
    /// <param name="maxAttempts">
    /// Total number of attempts (the initial try plus retries). Must be at least 1.
    /// </param>
    /// <param name="baseDelay">
    /// Base backoff delay. The delay before retry <c>n</c> (1-based) is
    /// <c>baseDelay * 2^(n-1)</c> scaled by a jitter factor in <c>[1, 2)</c>.
    /// </param>
    /// <param name="delay">
    /// Optional asynchronous delay used between attempts. Defaults to <see cref="Task.Delay(TimeSpan, CancellationToken)"/>.
    /// Tests pass a no-op to avoid real waiting.
    /// </param>
    /// <param name="jitter">
    /// Optional source of a jitter fraction in <c>[0, 1)</c>. Defaults to a shared
    /// <see cref="Random"/>. Tests pass a deterministic value.
    /// </param>
    /// <param name="maxDelay">
    /// Optional maximum delay between attempts. Defaults to <c>30s</c>.
    /// </param>
    public RetryingConnectionOpener(
        ITransientErrorDetector detector,
        int maxAttempts,
        TimeSpan baseDelay,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        Func<double>? jitter = null,
        TimeSpan? maxDelay = null)
    {
        if (detector is null)
        {
            throw new ArgumentNullException(nameof(detector));
        }

        if (maxAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAttempts), maxAttempts, "Max attempts must be at least 1.");
        }

        if (baseDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(baseDelay), baseDelay, "Base delay cannot be negative.");
        }

        var effectiveMaxDelay = maxDelay ?? TimeSpan.FromSeconds(30);
        if (effectiveMaxDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDelay), effectiveMaxDelay, "Max delay cannot be negative.");
        }

        _detector = detector;
        _maxAttempts = maxAttempts;
        _baseDelay = baseDelay;
        _maxDelay = effectiveMaxDelay;
        _delay = delay ?? ((d, ct) => Task.Delay(d, ct));
        _jitter = jitter ?? (() => Random.Shared.NextDouble());
    }

    /// <summary>
    /// Invokes <paramref name="open"/>, retrying transient failures up to the configured attempt
    /// cap. The most recent exception is rethrown when the cap is reached or a non-transient fault
    /// occurs.
    /// </summary>
    public async ValueTask<DbConnection> OpenAsync(
        Func<CancellationToken, ValueTask<DbConnection>> open,
        CancellationToken cancellationToken = default)
    {
        if (open is null)
        {
            throw new ArgumentNullException(nameof(open));
        }

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await open(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (attempt < _maxAttempts && _detector.IsTransient(ex))
            {
                await _delay(NextDelay(attempt), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private TimeSpan NextDelay(int attempt)
    {
        // Exponential backoff with a jitter factor in [1, 2). Computed in ticks to keep the
        // arithmetic deterministic given a fixed jitter source.
        if (_baseDelay == TimeSpan.Zero || _maxDelay == TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        var jitter = _jitter();
        if (double.IsNaN(jitter) || double.IsInfinity(jitter) || jitter < 0.0 || jitter >= 1.0)
        {
            throw new InvalidOperationException("Retry jitter must return a finite value in the range [0, 1).");
        }

        var factor = Math.Pow(2, attempt - 1) * (1.0 + jitter);
        var ticks = _baseDelay.Ticks * factor;
        if (double.IsNaN(ticks) || double.IsInfinity(ticks) || ticks >= _maxDelay.Ticks)
        {
            return _maxDelay;
        }

        return TimeSpan.FromTicks((long)ticks);
    }
}
