using Microsoft.AspNetCore.Http;

namespace Inquiry.AspNetCore;

/// <summary>
/// Middleware that opens an <see cref="InquiryAuditContext"/> scope for each request, stamping
/// <c>[InquiryCreatedBy]</c> / <c>[InquiryModifiedBy]</c> columns with the resolved user identity.
/// </summary>
public sealed class InquiryAuditContextMiddleware
{
    private readonly RequestDelegate _next;
    private readonly Func<HttpContext, string?> _userResolver;

    /// <summary>Initializes a new instance of the <see cref="InquiryAuditContextMiddleware"/> class.</summary>
    public InquiryAuditContextMiddleware(RequestDelegate next, Func<HttpContext, string?> userResolver)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _userResolver = userResolver ?? throw new ArgumentNullException(nameof(userResolver));
    }

    /// <summary>Invokes the middleware, wrapping downstream execution in an audit scope.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        using (InquiryAuditContext.BeginScope(_userResolver(context)))
        {
            await _next(context);
        }
    }
}
