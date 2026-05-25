using Inquiry.DependencyInjection;
using Inquiry.Sample.Data;
using Inquiry.Sample.Services;
using Inquiry.SqlServer.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("InquirySample");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("Missing connection string 'InquirySample'. Add it to appsettings.json under ConnectionStrings.");
}

await SampleDatabase.CreateSchemaAsync(connectionString);

builder.Services
    .AddInquiry()
    .AddInquirySqlServer(connectionString);

// One service per domain. Pages depend on these, not on stores directly.
builder.Services.AddScoped<OrganizationService>();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<MembershipService>();
builder.Services.AddScoped<CatalogService>();
builder.Services.AddScoped<TransactionDemoService>();
builder.Services.AddScoped<DataSeeder>();

// Blazor Server pipeline.
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();

var app = builder.Build();

// Seed sample data on first run.
using (var scope = app.Services.CreateScope())
{
    var seeder = scope.ServiceProvider.GetRequiredService<DataSeeder>();
    await seeder.SeedAsync();
}

app.UseStaticFiles();
app.UseRouting();
app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();
