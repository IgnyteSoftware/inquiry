using Inquiry.Sample;
using Inquiry.Sample.Services;

var builder = WebApplication.CreateBuilder(args);

// Pick the Inquiry provider (Sqlite/SqlServer/PostgreSql), ensure the target database
// and Northwind schema exist, and register the matching DI services.
await InquiryProviderSetup.ConfigureAsync(builder);

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
