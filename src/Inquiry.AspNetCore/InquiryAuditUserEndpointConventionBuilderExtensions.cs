using Microsoft.AspNetCore.Builder;

namespace Inquiry.AspNetCore;

/// <summary>
/// Extension methods that add <see cref="InquiryAuditUserAttribute"/> metadata to endpoints and route groups.
/// </summary>
public static class InquiryAuditUserEndpointConventionBuilderExtensions
{
    /// <summary>
    /// Sets the audit user for the endpoints that <paramref name="builder"/> configures, such as every
    /// endpoint in a route group. See <see cref="InquiryAuditUserAttribute"/> for the precedence rules.
    /// </summary>
    /// <param name="builder">The endpoint or route group builder.</param>
    /// <param name="user">The audit user, such as <c>system:kiosk</c>.</param>
    /// <param name="overrideResolver">
    /// <see langword="true"/> to use <paramref name="user"/> even when the user resolver returns a user.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="user"/> is null, empty or white space.</exception>
    public static TBuilder WithInquiryAuditUser<TBuilder>(this TBuilder builder, string user, bool overrideResolver = false)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.WithMetadata(new InquiryAuditUserAttribute(user) { OverrideResolver = overrideResolver });
    }
}
