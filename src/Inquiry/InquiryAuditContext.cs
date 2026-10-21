namespace Inquiry;

/// <summary>
/// Ambient current-user accessor for <c>[InquiryCreatedBy]</c> / <c>[InquiryModifiedBy]</c>
/// auditing columns. Set the user for the current async flow (typically once per request, in
/// middleware) with <see cref="BeginScope"/>; generated insert/update methods stamp the auditing
/// columns from <see cref="CurrentUser"/>. Flows across <see langword="await"/> boundaries via
/// <see cref="AsyncLocal{T}"/> and is isolated per async context, so concurrent requests don't
/// see each other's user.
/// </summary>
public static class InquiryAuditContext
{
    private static readonly AsyncLocal<ScopeFrame?> _current = new();

    /// <summary>
    /// Gets the current user identifier for the active async flow, or <see langword="null"/> when
    /// none is set. Auditing columns are stamped with this value.
    /// </summary>
    public static string? CurrentUser => _current.Value?._value;

    /// <summary>
    /// Sets <see cref="CurrentUser"/> for the current async flow and returns a scope that restores
    /// the previous value when disposed. Wrap a request (or unit of work) in the scope:
    /// <c>using (InquiryAuditContext.BeginScope(userId)) { … }</c>.
    /// </summary>
    public static Scope BeginScope(string? user)
    {
        var frame = new ScopeFrame(user, _current.Value);
        _current.Value = frame;
        return new Scope(frame);
    }

    internal sealed class ScopeFrame
    {
        internal readonly string? _value;
        internal readonly ScopeFrame? _previous;

        internal ScopeFrame(string? value, ScopeFrame? previous)
        {
            _value = value;
            _previous = previous;
        }
    }

    /// <summary>Restores the previous ambient value in the current async flow.</summary>
    public readonly struct Scope : System.IDisposable
    {
        private readonly ScopeFrame? _frame;

        internal Scope(ScopeFrame frame) => _frame = frame;

        /// <summary>
        /// Closes this scope. Repeated or copied disposal is a no-op in a flow where it is absent.
        /// </summary>
        /// <exception cref="System.InvalidOperationException">
        /// A nested scope is still active. Dispose nested scopes first, then retry.
        /// </exception>
        public void Dispose()
        {
            if (_frame is null)
            {
                return;
            }

            var current = _current.Value;
            if (ReferenceEquals(current, _frame))
            {
                _current.Value = _frame._previous;
                return;
            }

            for (var frame = current; frame is not null; frame = frame._previous)
            {
                if (ReferenceEquals(frame, _frame))
                {
                    throw new System.InvalidOperationException("Dispose nested ambient scopes before their parent scope.");
                }
            }
        }
    }
}
