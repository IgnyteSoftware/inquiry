using System.Collections.Concurrent;

namespace Inquiry.Interceptors;

/// <summary>
/// Scopes N+1 query detection to a logical operation (an HTTP request, a test, a job step).
/// Within a scope the <see cref="NPlusOneDetectionInterceptor"/> counts how many times each
/// distinct SQL fingerprint executes and warns when the count reaches the configured threshold.
/// Outside a scope the interceptor is a single <see langword="null"/> check — zero allocation,
/// zero cost.
/// </summary>
public sealed class InquiryNPlusOneScope : IDisposable
{
    private static readonly AsyncLocal<InquiryNPlusOneScope?> _current = new();

    internal ConcurrentDictionary<string, int> Counts { get; } = new(StringComparer.Ordinal);
    internal ConcurrentDictionary<string, byte> Warned { get; } = new(StringComparer.Ordinal);

    private readonly InquiryNPlusOneScope? _previous;
    private int _disposed;

    private InquiryNPlusOneScope(InquiryNPlusOneScope? previous) => _previous = previous;

    internal static InquiryNPlusOneScope? Current => _current.Value;

    /// <summary>
    /// Begins a new detection scope. Dispose the returned scope to end detection. Begin and
    /// dispose must occur in the same async flow (same <see cref="AsyncLocal{T}"/> context).
    /// Nested scopes are independent — queries inside a child scope do not count toward the parent.
    /// </summary>
    public static InquiryNPlusOneScope BeginScope()
    {
        var scope = new InquiryNPlusOneScope(_current.Value);
        _current.Value = scope;
        return scope;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            _current.Value = _previous;
    }
}
