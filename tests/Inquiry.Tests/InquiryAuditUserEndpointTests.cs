using System.Security.Claims;
using Inquiry.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;

namespace Inquiry.Tests;

public sealed class InquiryAuditUserEndpointTests
{
    private const string UserHeader = "X-Test-User";

    [Fact]
    public async Task RouteGroupMetadata_SetsUserForAnonymousRequest()
    {
        await using var app = await StartAsync(app =>
        {
            var kiosk = app.MapGroup("/kiosk").WithInquiryAuditUser("system:kiosk");
            kiosk.MapGet("/a", CurrentUser);
            kiosk.MapPost("/b", CurrentUser);
        });
        var client = app.GetTestClient();

        Assert.Equal("system:kiosk", await client.GetStringAsync("/kiosk/a"));
        var post = await client.PostAsync("/kiosk/b", content: null);
        Assert.Equal("system:kiosk", await post.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SignedInUser_OnNormalEndpoint_IsUnchanged()
    {
        await using var app = await StartAsync(app =>
        {
            app.MapGroup("/kiosk").WithInquiryAuditUser("system:kiosk").MapGet("/a", CurrentUser);
            app.MapGet("/normal", CurrentUser);
        });

        Assert.Equal("alice", await GetAsync(app, "/normal", "alice"));
        Assert.Equal("<null>", await GetAsync(app, "/normal", user: null));
    }

    [Fact]
    public async Task SignedInUser_WinsOverMetadata_UnlessMetadataOverrides()
    {
        await using var app = await StartAsync(app =>
        {
            app.MapGet("/fallback", CurrentUser).WithInquiryAuditUser("system:kiosk");
            app.MapGet("/override", CurrentUser).WithInquiryAuditUser("system:webhook", overrideResolver: true);
        });

        Assert.Equal("alice", await GetAsync(app, "/fallback", "alice"));
        Assert.Equal("system:webhook", await GetAsync(app, "/override", "alice"));
    }

    [Fact]
    public async Task HandlerScope_OverridesMetadata()
    {
        await using var app = await StartAsync(app =>
            app.MapGroup("/kiosk").WithInquiryAuditUser("system:kiosk", overrideResolver: true).MapGet("/a", () =>
            {
                using (InquiryAuditContext.BeginScope("device:42"))
                {
                    return InquiryAuditContext.CurrentUser;
                }
            }));

        Assert.Equal("device:42", await GetAsync(app, "/kiosk/a", "alice"));
    }

    [Fact]
    public async Task EndpointMetadata_WinsOverGroupMetadata()
    {
        await using var app = await StartAsync(app =>
        {
            var group = app.MapGroup("/g").WithInquiryAuditUser("system:group");
            group.MapGet("/inner", CurrentUser).WithInquiryAuditUser("system:inner");
            group.MapGet("/attribute", [InquiryAuditUser("system:attribute")] () => CurrentUser());
        });

        Assert.Equal("system:inner", await GetAsync(app, "/g/inner", user: null));
        Assert.Equal("system:attribute", await GetAsync(app, "/g/attribute", user: null));
    }

    [Fact]
    public async Task MiddlewareBeforeRouting_DoesNotSeeMetadata()
    {
        await using var app = await StartAsync(
            app => app.MapGet("/kiosk", CurrentUser).WithInquiryAuditUser("system:kiosk"),
            auditBeforeRouting: true);

        Assert.Equal("<null>", await GetAsync(app, "/kiosk", user: null));
    }

    [Fact]
    public void Attribute_RejectsBlankUser()
    {
        Assert.Throws<ArgumentException>(() => new InquiryAuditUserAttribute(" "));
        Assert.Throws<ArgumentNullException>(() => new InquiryAuditUserAttribute(null!));
    }

    private static string CurrentUser() => InquiryAuditContext.CurrentUser ?? "<null>";

    private static async Task<string> GetAsync(WebApplication app, string path, string? user)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (user is not null)
        {
            request.Headers.Add(UserHeader, user);
        }

        var response = await app.GetTestClient().SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    private static async Task<WebApplication> StartAsync(Action<WebApplication> map, bool auditBeforeRouting = false)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        var app = builder.Build();

        // Stands in for authentication: the header becomes the signed-in principal.
        app.Use((context, next) =>
        {
            if (context.Request.Headers.TryGetValue(UserHeader, out var user))
            {
                context.User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, user.ToString())],
                    "test"));
            }

            return next(context);
        });

        if (auditBeforeRouting)
        {
            app.UseInquiryAuditContext();
            app.UseRouting();
        }
        else
        {
            app.UseRouting();
            app.UseInquiryAuditContext();
        }

        map(app);
        await app.StartAsync();
        return app;
    }
}
