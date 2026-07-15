using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Inquiry.AspNetCore;

/// <summary>
/// Extension methods for registering <see cref="InquiryAuditContextMiddleware"/> in the
/// ASP.NET Core request pipeline.
/// </summary>
public static class InquiryAuditContextApplicationBuilderExtensions
{
    /// <summary>
    /// Adds middleware that opens an <see cref="InquiryAuditContext"/> scope for each request.
    /// The <paramref name="userResolver"/> callback extracts the user identifier from the
    /// <see cref="HttpContext"/>; when omitted, the middleware defaults to
    /// <see cref="ClaimTypes.NameIdentifier"/>.
    /// </summary>
    /// <remarks>
    /// Register this middleware <b>after</b> <c>UseAuthentication</c> so that
    /// <see cref="HttpContext.User"/> is populated when the resolver runs.
    /// </remarks>
    public static IApplicationBuilder UseInquiryAuditContext(
        this IApplicationBuilder app,
        Func<HttpContext, string?>? userResolver = null)
    {
        if (app is null) throw new ArgumentNullException(nameof(app));

        userResolver ??= static ctx => ctx.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return app.UseMiddleware<InquiryAuditContextMiddleware>(userResolver);
    }
}
