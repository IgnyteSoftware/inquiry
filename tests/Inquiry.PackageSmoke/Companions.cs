using Inquiry;
using Inquiry.AspNetCore;
using Inquiry.Commands;
using Inquiry.DependencyInjection;
using Inquiry.Interceptors;
using Inquiry.Sqlite.Testing;
using Inquiry.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

[assembly: InquiryDialect("Sqlite")]

var recorder = new RecordingCommandInterceptor();
await using var fixture = await SqliteInquiryFixture.CreateAsync(services => services
    .AddInquirySqlCommenter("package-consumer")
    .AddSingleton<IInquiryCommandInterceptor>(recorder));
await fixture.ExecuteDdlAsync("CREATE TABLE widgets (id INTEGER PRIMARY KEY, name TEXT NOT NULL)");
using (var scope = fixture.CreateScope())
{
    var inquiry = scope.ServiceProvider.GetRequiredService<IInquiry>();
    var affected = await inquiry.ExecuteAsync(new InquiryCommand("INSERT INTO widgets VALUES (1, 'Anvil')"));
    if (affected != 1) throw new InvalidOperationException("Fixture insert did not affect one row.");
    recorder.AssertExecuted("INSERT INTO widgets");
    recorder.AssertExecuted("application='package-consumer'");
}

var middleware = new InquiryAuditContextMiddleware(_ =>
{
    if (InquiryAuditContext.CurrentUser != "package-user") throw new InvalidOperationException("Missing audit identity.");
    return Task.CompletedTask;
}, _ => "package-user");
await middleware.InvokeAsync(new DefaultHttpContext());
if (InquiryAuditContext.CurrentUser is not null) throw new InvalidOperationException("Audit scope leaked.");

var builder = Host.CreateApplicationBuilder();
builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
{
    ["ConnectionStrings:widgets"] = fixture.ConnectionString,
});
builder.Services.AddInquiry();
builder.AddInquirySqlite("widgets");
using var host = builder.Build();
var health = await host.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync();
if (!health.Entries.ContainsKey("inquiry") || health.Status != HealthStatus.Healthy)
    throw new InvalidOperationException("Aspire resource health check failed.");
Console.WriteLine("COMPANIONS-OK: SQLite fixture, command recording, sqlcommenter, audit middleware, Aspire health check.");
