using Inquiry;
using Inquiry.DependencyInjection;
using Inquiry.Sample.Data;
using Inquiry.Sample.Models;
using Inquiry.Sample.Stores;
using Inquiry.Sqlite.DependencyInjection;
using Microsoft.Data.Sqlite;

var databasePath = Path.Combine(AppContext.BaseDirectory, "inquiry-sample.db");
if (File.Exists(databasePath))
{
    File.Delete(databasePath);
}

var connectionString = new SqliteConnectionStringBuilder
{
    DataSource = databasePath,
}.ToString();

await SampleDatabase.CreateSchemaAsync(connectionString);

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddInquiry()
    .AddInquirySqlite(connectionString);

var app = builder.Build();

// ── Seed data on startup ─────────────────────────────────────────────────────
await SeedAsync(app.Services);

// ── HTML dashboard ───────────────────────────────────────────────────────────
app.MapGet("/", async (
    OrganizationStore orgs,
    UserStore users,
    OrganizationToUserStore memberships,
    CategoryStore categories,
    ProductStore products) =>
{
    var allOrgs = await ToListAsync(orgs.SelectAllAsync());
    var allUsers = await ToListAsync(users.SelectAllAsync());
    var allMemberships = await ToListAsync(memberships.SelectAllAsync());
    var categoriesWithProducts = await ToListAsync(categories.SelectAllWithProductsAsync());

    return Results.Content(BuildHtml(allOrgs, allUsers, allMemberships, categoriesWithProducts), "text/html");
});

// ── JSON API endpoints ────────────────────────────────────────────────────────

app.MapGet("/api/organizations", async (OrganizationStore store) =>
    await ToListAsync(store.SelectAllAsync()));

app.MapGet("/api/organizations/{key:guid}", async (Guid key, OrganizationStore store) =>
{
    var org = await store.SelectByKeyAsync(key);
    return org is null ? Results.NotFound() : Results.Ok(org);
});

app.MapGet("/api/categories", async (CategoryStore store) =>
    await ToListAsync(store.SelectAllWithProductsAsync()));

app.MapGet("/api/categories/{key:guid}", async (Guid key, CategoryStore store) =>
{
    var cat = await store.SelectByKeyWithProductsAsync(key);
    return cat is null ? Results.NotFound() : Results.Ok(cat);
});

app.MapGet("/api/products", async (ProductStore store) =>
    await ToListAsync(store.SelectAllAsync()));

app.MapPut("/api/products/{key:guid}", async (Guid key, Product updated, ProductStore store) =>
{
    updated.Key = key;
    var rows = await store.UpsertAsync(updated);
    return Results.Ok(new { rows });
});

// ── Transaction demo endpoint ─────────────────────────────────────────────────
app.MapPost("/api/demo/transaction", async (IInquiry inquiry, CategoryStore catStore, ProductStore prodStore) =>
{
    var category = new Category { Key = Guid.NewGuid(), Name = "Transaction Demo Category" };

    await using var tx = await inquiry.BeginTransactionAsync();
    try
    {
        await tx.Inquiry.ExecuteAsync(
            "INSERT INTO TCategory (Key, Name) VALUES (@Key, @Name)",
            new { category.Key, category.Name });

        var products = Enumerable.Range(1, 3).Select(i => new Product
        {
            Key = Guid.NewGuid(),
            Name = $"TX Product {i}",
            Price = i * 9.99m,
            CategoryKey = category.Key,
        }).ToList();

        foreach (var p in products)
        {
            await tx.Inquiry.ExecuteAsync(
                "INSERT INTO TProduct (Key, Name, Price, CategoryKey) VALUES (@Key, @Name, @Price, @CategoryKey)",
                new { p.Key, p.Name, p.Price, p.CategoryKey });
        }

        await tx.CommitAsync();
        return Results.Ok(new { message = "Transaction committed", categoryKey = category.Key, productsInserted = products.Count });
    }
    catch
    {
        await tx.RollbackAsync();
        throw;
    }
});

app.Run();

static async Task SeedAsync(IServiceProvider services)
{
    await using var scope = services.CreateAsyncScope();
    var orgs = scope.ServiceProvider.GetRequiredService<OrganizationStore>();
    var users = scope.ServiceProvider.GetRequiredService<UserStore>();
    var memberships = scope.ServiceProvider.GetRequiredService<OrganizationToUserStore>();
    var categories = scope.ServiceProvider.GetRequiredService<CategoryStore>();
    var products = scope.ServiceProvider.GetRequiredService<ProductStore>();
    var inquiry = scope.ServiceProvider.GetRequiredService<IInquiry>();

    // Organizations
    var acme = new Organization { Key = Guid.NewGuid(), Name = "Acme Research", IsActive = true };
    var globex = new Organization { Key = Guid.NewGuid(), Name = "Globex Industries", IsActive = true };
    await orgs.InsertAsync(acme);
    await orgs.InsertAsync(globex);

    // Users
    var alice = new User { Key = Guid.NewGuid(), FirstName = "Alice", LastName = "Anders", Email = "alice@example.com" };
    var bob = new User { Key = Guid.NewGuid(), FirstName = "Bob", LastName = "Brown", Email = "bob@example.com" };
    var carol = new User { Key = Guid.NewGuid(), FirstName = "Carol", LastName = "Carter", Email = "carol@example.com" };
    foreach (var u in new[] { alice, bob, carol })
    {
        await users.InsertAsync(u);
    }

    // Memberships
    foreach (var m in new[]
    {
        new OrganizationToUser { Key = Guid.NewGuid(), OrganizationKey = acme.Key, UserKey = alice.Key, IsActive = true },
        new OrganizationToUser { Key = Guid.NewGuid(), OrganizationKey = acme.Key, UserKey = bob.Key, IsActive = true },
        new OrganizationToUser { Key = Guid.NewGuid(), OrganizationKey = globex.Key, UserKey = alice.Key, IsActive = true },
        new OrganizationToUser { Key = Guid.NewGuid(), OrganizationKey = globex.Key, UserKey = carol.Key, IsActive = true },
    })
    {
        await memberships.InsertAsync(m);
    }

    // Categories and products
    var electronics = new Category { Key = Guid.NewGuid(), Name = "Electronics" };
    var clothing = new Category { Key = Guid.NewGuid(), Name = "Clothing" };
    await categories.InsertAsync(electronics);
    await categories.InsertAsync(clothing);

    foreach (var p in new[]
    {
        new Product { Key = Guid.NewGuid(), Name = "Laptop", Price = 999.99m, CategoryKey = electronics.Key },
        new Product { Key = Guid.NewGuid(), Name = "Phone", Price = 699.99m, CategoryKey = electronics.Key },
        new Product { Key = Guid.NewGuid(), Name = "Headphones", Price = 149.99m, CategoryKey = electronics.Key },
        new Product { Key = Guid.NewGuid(), Name = "T-Shirt", Price = 29.99m, CategoryKey = clothing.Key },
        new Product { Key = Guid.NewGuid(), Name = "Jeans", Price = 59.99m, CategoryKey = clothing.Key },
    })
    {
        await products.InsertAsync(p);
    }

    // Demonstrate upsert: update one product
    var allProducts = await ToListAsync(products.SelectAllAsync());
    if (allProducts.Count > 0)
    {
        var laptop = allProducts.FirstOrDefault(p => p.Name == "Laptop");
        if (laptop is not null)
        {
            laptop.Price = 899.99m; // Sale price
            await products.UpsertAsync(laptop);
        }
    }
}

static string BuildHtml(
    List<Organization> orgs,
    List<User> users,
    List<OrganizationToUser> memberships,
    List<Category> categoriesWithProducts)
{
    static string Rows<T>(List<T> items, Func<T, string> rowHtml)
        => items.Count == 0 ? "<tr><td colspan='99' style='color:#888'>No data</td></tr>" : string.Concat(items.Select(rowHtml));

    var orgRows = Rows(orgs, o =>
        $"<tr><td>{o.Key:N}</td><td>{System.Web.HttpUtility.HtmlEncode(o.Name)}</td><td>{(o.IsActive ? "✓" : "✗")}</td></tr>");

    var userRows = Rows(users, u =>
        $"<tr><td>{u.Key:N}</td><td>{System.Web.HttpUtility.HtmlEncode(u.FirstName)} {System.Web.HttpUtility.HtmlEncode(u.LastName)}</td><td>{System.Web.HttpUtility.HtmlEncode(u.Email)}</td></tr>");

    var catRows = string.Concat(categoriesWithProducts.Select(c =>
    {
        var prodRows = c.Products is null || c.Products.Count == 0
            ? "<em>no products</em>"
            : string.Join(", ", c.Products.Select(p => $"{System.Web.HttpUtility.HtmlEncode(p.Name)} (${p.Price:0.00})"));
        return $"<tr><td>{System.Web.HttpUtility.HtmlEncode(c.Name)}</td><td>{prodRows}</td></tr>";
    }));

    return $$"""
        <!DOCTYPE html>
        <html lang="en">
        <head>
          <meta charset="utf-8"/>
          <title>Inquiry Sample</title>
          <style>
            body{font-family:system-ui,sans-serif;max-width:1100px;margin:2rem auto;padding:0 1rem;color:#222}
            h1{color:#0070f3}h2{margin-top:2rem;border-bottom:1px solid #e5e7eb;padding-bottom:.4rem}
            table{width:100%;border-collapse:collapse;margin-top:.8rem}
            th,td{text-align:left;padding:.45rem .8rem;border:1px solid #e5e7eb}
            th{background:#f9fafb;font-weight:600}
            tr:hover td{background:#f0f7ff}
            .badge{display:inline-block;padding:.15rem .6rem;border-radius:999px;font-size:.8em;background:#dcfce7;color:#166534}
            .api{background:#f0f9ff;border:1px solid #bae6fd;border-radius:.5rem;padding:1rem;margin-top:2rem}
            .api h3{margin:0 0 .5rem}code{background:#f1f5f9;padding:.1rem .4rem;border-radius:3px;font-size:.9em}
          </style>
        </head>
        <body>
          <h1>🔍 Inquiry Sample Application</h1>
          <p>Source-generated micro-ORM demonstrating: <strong>CRUD · Upsert · Transactions · Eager Loading</strong></p>

          <h2>Organizations</h2>
          <table>
            <tr><th>Key</th><th>Name</th><th>Active</th></tr>
            {{orgRows}}
          </table>

          <h2>Users</h2>
          <table>
            <tr><th>Key</th><th>Name</th><th>Email</th></tr>
            {{userRows}}
          </table>

          <h2>Categories &amp; Products <span class="badge">Eager Loaded</span></h2>
          <p>Products are loaded via <code>[InquiryRelation]</code> + <code>[InquirySelectAllEager]</code> in a single coordinated query.</p>
          <table>
            <tr><th>Category</th><th>Products</th></tr>
            {{catRows}}
          </table>

          <div class="api">
            <h3>REST API Endpoints</h3>
            <ul>
              <li><code>GET /api/organizations</code> — list all organizations</li>
              <li><code>GET /api/organizations/{key}</code> — find one by key</li>
              <li><code>GET /api/categories</code> — list categories with products (eager)</li>
              <li><code>GET /api/products</code> — list all products</li>
              <li><code>PUT /api/products/{key}</code> — upsert a product</li>
              <li><code>POST /api/demo/transaction</code> — insert category + products in a transaction</li>
            </ul>
          </div>
        </body>
        </html>
        """;
}

static async Task<List<T>> ToListAsync<T>(IAsyncEnumerable<T> source)
{
    var list = new List<T>();
    await foreach (var item in source) list.Add(item);
    return list;
}
