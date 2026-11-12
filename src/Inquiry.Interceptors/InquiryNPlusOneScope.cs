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

    /// <summary>
    /// Ends detection for this scope and makes the enclosing scope active again. Repeated disposal is a
    /// no-op, and so is disposal in an async flow where this scope is not active.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A nested scope is still active. Dispose nested scopes first, then retry.
    /// </exception>
    public void Dispose()
    {
        var current = _current.Value;
        if (ReferenceEquals(current, this))
        {
            _current.Value = _previous;
            return;
        }

        for (var scope = current; scope is not null; scope = scope._previous)
        {
            if (ReferenceEquals(scope, this))
            {
                // Disposing out of order would leave the wrong scope active, so misuse fails loudly.
#pragma warning disable S3877
                throw new InvalidOperationException("Dispose nested N+1 detection scopes before their parent scope.");
#pragma warning restore S3877
            }
        }
    }
}
