namespace Inquiry;

/// <summary>
/// Ambient filter-value accessor for runtime-parameterized <c>[InquiryGlobalFilter]</c> columns
/// (<c>ContextKey = "…"</c>). Set the values for the current async flow (typically once per request,
/// in middleware) with <see cref="BeginScope"/>; generated methods whose SQL composes a parameterized
/// filter bind the value at execute time, immediately before the command runs. Flows across
/// <see langword="await"/> boundaries via <see cref="AsyncLocal{T}"/> and is isolated per async
/// context, so concurrent requests don't see each other's values. Modeled on
/// <see cref="InquiryAuditContext"/>.
/// </summary>
public static class InquiryFilterContext
{
    private static readonly AsyncLocal<ScopeFrame?> _current = new();

    /// <summary>
    /// Sets the ambient filter values for the current async flow and returns a scope that restores
    /// the previous values when disposed. Keys are the <c>ContextKey</c> strings declared on the
    /// filter columns; a scope REPLACES the ambient dictionary rather than merging, so nest scopes
    /// deliberately: <c>using (InquiryFilterContext.BeginScope(new Dictionary&lt;string, object&gt; {
    /// ["TenantId"] = tenantId })) { … }</c>.
    /// </summary>
    public static Scope BeginScope(IReadOnlyDictionary<string, object> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        // Snapshot rather than alias: AsyncLocal isolates the REFERENCE per async flow, not the
        // contents. A caller that reused or later mutated its dictionary (a pooled scratch dict, a
        // singleton field) would otherwise hand every concurrent request the same mutable tenant
        // state — a silent cross-tenant read with no exception to notice.
        var snapshot = new Dictionary<string, object>(values.Count, System.StringComparer.Ordinal);
        foreach (var pair in values)
        {
            snapshot[pair.Key] = pair.Value;
        }

        var frame = new ScopeFrame(snapshot, _current.Value);
        _current.Value = frame;
        return new Scope(frame);
    }

    /// <summary>
    /// Gets the ambient value for <paramref name="contextKey"/>, throwing
    /// <see cref="InquiryFilterValueMissingException"/> when no scope is active, the key is absent,
    /// or the value is null / not a <typeparamref name="T"/>. Called by generated binders — a
    /// missing tenant value must fail the command BEFORE it executes; binding null or skipping the
    /// parameter would silently return no rows (or worse, unfiltered rows) instead of surfacing the
    /// missing scope. The failing value is never included in the exception message.
    /// </summary>
    public static T GetRequired<T>(string contextKey)
    {
        var values = _current.Value?._value;
        if (values is null)
        {
            throw new InquiryFilterValueMissingException(
                $"No ambient filter scope is active for filter context key '{contextKey}'. Wrap the call in InquiryFilterContext.BeginScope(...) — typically once per request, in middleware.");
        }

        if (!values.TryGetValue(contextKey, out var value) || value is null)
        {
            throw new InquiryFilterValueMissingException(
                $"The active filter scope has no value for context key '{contextKey}'. Add it to the dictionary passed to InquiryFilterContext.BeginScope(...).");
        }

        if (value is not T typed)
        {
            throw new InquiryFilterValueMissingException(
                $"The ambient value for filter context key '{contextKey}' is a {value.GetType().Name}, but the filter column requires {typeof(T).Name}.");
        }

        return typed;
    }

    internal sealed class ScopeFrame
    {
        internal readonly IReadOnlyDictionary<string, object> _value;
        internal readonly ScopeFrame? _previous;

        internal ScopeFrame(IReadOnlyDictionary<string, object> value, ScopeFrame? previous)
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
