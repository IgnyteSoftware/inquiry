using System;
using System.Collections.Concurrent;
using System.Threading;

namespace Inquiry.Interceptors;

/// <summary>
/// Delimits an N+1 detection scope for <see cref="NPlusOneDetectionInterceptor"/>. Open one around a
/// logical unit of work (an HTTP request, a job, a test) — the interceptor counts how often each
/// distinct SQL statement runs <em>within the active scope</em> and warns once a statement repeats past
/// the threshold (the classic N+1 signature: one parent query then N child queries with the same SQL and
/// different parameters). Outside any scope the interceptor is a no-op, so detection is opt-in per scope.
/// </summary>
/// <remarks>
/// Backed by an <see cref="AsyncLocal{T}"/>, so the scope flows through awaits down to the interceptor.
/// Scopes nest: <see cref="BeginScope"/> starts a fresh count and restores the previous scope on dispose.
/// </remarks>
public static class InquiryNPlusOneScope
{
    private static readonly AsyncLocal<ScopeState?> Active = new();

    internal static ScopeState? Current => Active.Value;

    /// <summary>
    /// Begins a new detection scope with an empty statement count. Dispose the returned handle (typically
    /// via <c>using</c>) to end the scope and restore any enclosing one.
    /// </summary>
    public static IDisposable BeginScope()
    {
        var previous = Active.Value;
        Active.Value = new ScopeState();
        return new ScopeHandle(previous);
    }

    internal sealed class ScopeState
    {
        // Concurrent so queries fanned out in parallel within one scope (Task.WhenAll) count safely.
        public ConcurrentDictionary<string, int> Counts { get; } = new(StringComparer.Ordinal);
    }

    private sealed class ScopeHandle : IDisposable
    {
        private readonly ScopeState? _previous;
        private bool _disposed;

        public ScopeHandle(ScopeState? previous) => _previous = previous;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Active.Value = _previous;
        }
    }
}
