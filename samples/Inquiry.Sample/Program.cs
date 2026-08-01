using Inquiry.DependencyInjection;
using Inquiry.Sample;
using Inquiry.Sample.Services;
using Inquiry.Seeding;

var builder = WebApplication.CreateBuilder(args);

// Pick the Inquiry provider (Sqlite/SqlServer/PostgreSql), ensure the target database
// and Northwind schema exist, and register the matching DI services.
await InquiryProviderSetup.ConfigureAsync(builder);

// Observability: spans on the "Inquiry" ActivitySource, a db.client.operation.duration histogram
// on the "Inquiry" Meter, and per-command logs on the "Inquiry.Command" category (set it to Debug
// in appsettings to see them). Subscribe an OpenTelemetry TracerProvider/MeterProvider to
// InquiryTelemetry.ActivitySourceName / MeterName to export the spans and metrics.
builder.Services.AddInquiryTelemetry();

// Liveness/readiness: opens a connection through the registered Inquiry connection factory.
builder.Services.AddHealthChecks().AddInquiry();

// One service per domain. Pages depend on these, not on stores directly.
builder.Services.AddScoped<CustomerService>();
builder.Services.AddScoped<EmployeeService>();
builder.Services.AddScoped<CatalogService>();
builder.Services.AddScoped<SupplierService>();
builder.Services.AddScoped<ShipperService>();
builder.Services.AddScoped<RegionService>();
builder.Services.AddScoped<OrderService>();
builder.Services.AddScoped<DemographicsService>();
builder.Services.AddScoped<EmployeeTerritoryService>();
builder.Services.AddScoped<OrderTransactionService>();
builder.Services.AddInquirySeeder<DataSeeder>();

// Blazor Server pipeline.
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();

var app = builder.Build();

// Seed sample data on first run (runs every registered IInquiryDataSeeder in one scope).
await app.Services.SeedInquiryAsync();

app.UseStaticFiles();
app.UseRouting();
app.MapHealthChecks("/health");
app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();
