using Inquiry.DependencyInjection;
using Inquiry.Northwind;
using Inquiry.Sample.Services;
using Inquiry.Sqlite.DependencyInjection;
using Microsoft.Data.Sqlite;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("InquirySample");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("Missing connection string 'InquirySample'. Add it to appsettings.json under ConnectionStrings.");
}

// Create the Northwind schema on first run. Idempotent — the DDL uses CREATE TABLE IF NOT EXISTS.
await using (var connection = new SqliteConnection(connectionString))
{
    await connection.OpenAsync();
    await using var command = connection.CreateCommand();
    command.CommandText = NorthwindSchema.SqliteDdl;
    await command.ExecuteNonQueryAsync();
}

builder.Services
    .AddInquiry()
    .AddInquirySqlite(connectionString);

// One service per domain. Pages depend on these, not on stores directly.
builder.Services.AddScoped<CustomerService>();
builder.Services.AddScoped<EmployeeService>();
builder.Services.AddScoped<CatalogService>();
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
