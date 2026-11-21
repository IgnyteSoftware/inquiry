using Inquiry.Connections;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

// run.ps1 replaces __PROVIDER__ in a copy of this file, so each consumer compiles against one Aspire provider package.
var builder = Host.CreateApplicationBuilder();
builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
{
    ["ConnectionStrings:orders"] = args[0],
});
builder.AddInquiry__PROVIDER__("orders");
builder.AddInquiryAspireDefaults();
using var host = builder.Build();

var factory = host.Services.GetRequiredService<IInquiryConnectionFactory>().GetType().Name;
if (factory != "__PROVIDER__InquiryConnectionFactory")
    throw new InvalidOperationException($"Unexpected connection factory {factory}.");
var healthChecks = host.Services.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;
if (healthChecks.Count(registration => registration.Name == "inquiry") != 1)
    throw new InvalidOperationException("The Aspire registration must add exactly one inquiry health check.");
Console.WriteLine("ASPIRE-__PROVIDER__-OK");
