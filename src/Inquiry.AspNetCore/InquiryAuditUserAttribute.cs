namespace Inquiry.AspNetCore;

/// <summary>
/// Endpoint metadata that sets the audit user for requests to an endpoint that has no signed-in
/// principal, such as a kiosk, device-token or webhook endpoint. <see cref="InquiryAuditContextMiddleware"/>
/// reads it from the routed endpoint.
/// </summary>
/// <remarks>
/// <para>
/// Apply it to an MVC controller or action, or add it to a minimal API endpoint or route group with
/// <see cref="InquiryAuditUserEndpointConventionBuilderExtensions.WithInquiryAuditUser{TBuilder}(TBuilder, string, bool)"/>.
/// When an endpoint has more than one, the one that ASP.NET Core adds last wins and replaces
/// <see cref="OverrideResolver"/> too. Conventions on an endpoint or on <c>MapControllers()</c>
/// are added after the attributes on a controller, action or handler.
/// </para>
/// <para>
/// The middleware uses <see cref="User"/> when the user resolver returns <see langword="null"/>, or
/// always when <see cref="OverrideResolver"/> is <see langword="true"/>. A scope that a handler opens
/// with <see cref="InquiryAuditContext.BeginScope"/> overrides both. The middleware sees endpoint
/// metadata only when it runs after <c>UseRouting</c>.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class InquiryAuditUserAttribute : Attribute
{
    /// <summary>Initializes a new instance of the <see cref="InquiryAuditUserAttribute"/> class.</summary>
    /// <param name="user">The audit user for the endpoint, such as <c>system:kiosk</c>.</param>
    /// <exception cref="ArgumentException"><paramref name="user"/> is null, empty or white space.</exception>
    public InquiryAuditUserAttribute(string user)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(user);
        User = user;
    }

    /// <summary>Gets the audit user for the endpoint.</summary>
    public string User { get; }

    /// <summary>
    /// Gets or sets a value that tells the middleware to use <see cref="User"/> even when the user
    /// resolver returns a user. The resolver is not called. The default is <see langword="false"/>.
    /// </summary>
    public bool OverrideResolver { get; set; }
}
