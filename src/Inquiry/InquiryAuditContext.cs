using System.Threading;

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
    private static readonly AsyncLocal<string?> Current = new();

    /// <summary>
    /// Gets the current user identifier for the active async flow, or <see langword="null"/> when
    /// none is set. Auditing columns are stamped with this value.
    /// </summary>
    public static string? CurrentUser => Current.Value;

    /// <summary>
    /// Sets <see cref="CurrentUser"/> for the current async flow and returns a scope that restores
    /// the previous value when disposed. Wrap a request (or unit of work) in the scope:
    /// <c>using (InquiryAuditContext.BeginScope(userId)) { … }</c>.
    /// </summary>
    public static Scope BeginScope(string? user)
    {
        var previous = Current.Value;
        Current.Value = user;
        return new Scope(previous);
    }

    /// <summary>Restores the previous <see cref="CurrentUser"/> when disposed.</summary>
    public readonly struct Scope : System.IDisposable
    {
        private readonly string? _previous;

        internal Scope(string? previous) => _previous = previous;

        /// <summary>Restores the user value captured when the scope was opened.</summary>
        public void Dispose() => Current.Value = _previous;
    }
}
