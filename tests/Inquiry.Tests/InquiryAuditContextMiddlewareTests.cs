using System.Security.Claims;
using Inquiry.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Inquiry.Tests;

public sealed class InquiryAuditContextMiddlewareTests
{
    [Fact]
    public async Task CustomResolver_SetsCurrentUser()
    {
        string? captured = null;
        var middleware = new InquiryAuditContextMiddleware(
            next: _ =>
            {
                captured = InquiryAuditContext.CurrentUser;
                return Task.CompletedTask;
            },
            userResolver: _ => "test-user");

        await middleware.InvokeAsync(new DefaultHttpContext());

        Assert.Equal("test-user", captured);
    }

    [Fact]
    public async Task DefaultResolver_UsesNameIdentifierClaim()
    {
        string? captured = null;
        RequestDelegate next = _ =>
        {
            captured = InquiryAuditContext.CurrentUser;
            return Task.CompletedTask;
        };

        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "user-from-claim")],
            "test-scheme"));

        var app = new ApplicationBuilder(new ServiceCollection().BuildServiceProvider());
        app.UseInquiryAuditContext();
        app.Run(next);

        var pipeline = app.Build();
        await pipeline(context);

        Assert.Equal("user-from-claim", captured);
    }

    [Fact]
    public async Task Scope_RestoredAfterRequest()
    {
        using (InquiryAuditContext.BeginScope("outer"))
        {
            var middleware = new InquiryAuditContextMiddleware(
                next: _ =>
                {
                    Assert.Equal("inner", InquiryAuditContext.CurrentUser);
                    return Task.CompletedTask;
                },
                userResolver: _ => "inner");

            await middleware.InvokeAsync(new DefaultHttpContext());

            Assert.Equal("outer", InquiryAuditContext.CurrentUser);
        }
    }

    [Fact]
    public async Task AnonymousRequest_SetsNullUser()
    {
        string? captured = "not-null";
        var middleware = new InquiryAuditContextMiddleware(
            next: _ =>
            {
                captured = InquiryAuditContext.CurrentUser;
                return Task.CompletedTask;
            },
            userResolver: _ => null);

        await middleware.InvokeAsync(new DefaultHttpContext());

        Assert.Null(captured);
    }

    [Fact]
    public async Task DefaultResolver_AnonymousUser_SetsNull()
    {
        string? captured = "not-null";
        RequestDelegate next = _ =>
        {
            captured = InquiryAuditContext.CurrentUser;
            return Task.CompletedTask;
        };

        var context = new DefaultHttpContext();

        var app = new ApplicationBuilder(new ServiceCollection().BuildServiceProvider());
        app.UseInquiryAuditContext();
        app.Run(next);

        var pipeline = app.Build();
        await pipeline(context);

        Assert.Null(captured);
    }

    [Fact]
    public async Task Scope_RestoredEvenOnException()
    {
        using (InquiryAuditContext.BeginScope("outer"))
        {
            var middleware = new InquiryAuditContextMiddleware(
                next: _ => throw new InvalidOperationException("boom"),
                userResolver: _ => "inner");

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => middleware.InvokeAsync(new DefaultHttpContext()));

            Assert.Equal("outer", InquiryAuditContext.CurrentUser);
        }
    }
}
