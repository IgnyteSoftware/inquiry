using Microsoft.AspNetCore.Http;

namespace Inquiry.AspNetCore;

/// <summary>
/// Middleware that opens an <see cref="InquiryAuditContext"/> scope for each request, stamping
/// <c>[InquiryCreatedBy]</c> / <c>[InquiryModifiedBy]</c> columns with the resolved user identity.
/// </summary>
/// <remarks>
/// When the routed endpoint has <see cref="InquiryAuditUserAttribute"/> metadata, the middleware uses
/// its user if the resolver returns <see langword="null"/>, or always if
/// <see cref="InquiryAuditUserAttribute.OverrideResolver"/> is set. Register the middleware after
/// <c>UseRouting</c> so that the endpoint is known.
/// </remarks>
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
        using (InquiryAuditContext.BeginScope(ResolveUser(context)))
        {
            await _next(context);
        }
    }

    private string? ResolveUser(HttpContext context)
    {
        var endpointUser = context.GetEndpoint()?.Metadata.GetMetadata<InquiryAuditUserAttribute>();
        if (endpointUser is { OverrideResolver: true })
        {
            return endpointUser.User;
        }

        return _userResolver(context) ?? endpointUser?.User;
    }
}
