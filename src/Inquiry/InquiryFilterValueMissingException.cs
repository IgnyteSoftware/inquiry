namespace Inquiry;

/// <summary>
/// Thrown by a generated method BEFORE its command executes when a runtime-parameterized
/// <c>[InquiryGlobalFilter(ContextKey = "…")]</c> column has no usable ambient value — no
/// <see cref="InquiryFilterContext.BeginScope"/> scope is active, the key is absent from the scope,
/// or the value's type does not match the column. Deliberately a distinct exception type: a missing
/// tenant scope is a configuration error the caller must be able to alert on, not a query that
/// happens to return nothing. Messages name the context key but never the value.
/// </summary>
public sealed class InquiryFilterValueMissingException : System.InvalidOperationException
{
    /// <summary>Initializes the exception with a message naming the context key and the remedy.</summary>
    public InquiryFilterValueMissingException(string message)
        : base(message)
    {
    }
}
