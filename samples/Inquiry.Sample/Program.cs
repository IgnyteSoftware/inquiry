using Inquiry.DependencyInjection;
using Inquiry.Sample.Data;
using Inquiry.Sample.Services;
using Inquiry.Sqlite.DependencyInjection;
using Microsoft.Data.Sqlite;

// ── 1. Build the SQLite connection string ────────────────────────────────────
var databasePath = Path.Combine(AppContext.BaseDirectory, "inquiry-sample.db");
if (File.Exists(databasePath))
{
    File.Delete(databasePath);
}

var connectionString = new SqliteConnectionStringBuilder
{
    DataSource = databasePath,
}.ToString();

// ── 2. Create the schema before the web host starts ──────────────────────────
await SampleDatabase.CreateSchemaAsync(connectionString);

// ── 3. Configure services ────────────────────────────────────────────────────
var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddInquiry()
    .AddInquirySqlite(connectionString);

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

// ── 4. Seed sample data on first run ─────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var seeder = scope.ServiceProvider.GetRequiredService<DataSeeder>();
    await seeder.SeedAsync();
}

// ── 5. Wire up the HTTP pipeline ─────────────────────────────────────────────
app.UseStaticFiles();
app.UseRouting();
app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();
