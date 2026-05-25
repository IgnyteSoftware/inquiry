# Inquiry.Sample

A Blazor Server application that exercises Inquiry against SQL Server.
Each page injects a small **service** that wraps one or two generated stores; no page talks
to a store directly. The seed data, the catalog with eager-loaded products, and the
transactional insert demo each live in their own service file.

## Running it

```powershell
dotnet run --project samples\Inquiry.Sample\Inquiry.Sample.csproj
```

The app reads `ConnectionStrings:InquirySample` from `appsettings.json`, creates the sample
tables if they do not already exist, seeds them via `DataSeeder`, then serves the Blazor UI
at the URL printed in the console.

## What it demonstrates

- Entity mapping with `[InquiryTable]`, `[InquiryKey]`, `[InquiryColumn]`, `[InquiryForeignKey]`, `[InquiryRelation]`
- Store generation with `[InquirySelectAll]`, `[InquirySelectOneByKey]`, `[InquirySelectAllByField]`, `[InquiryInsert]`, `[InquiryUpdate]`, `[InquiryUpsert]`, `[InquiryDeleteOneByKey]`, `[InquirySelectAllEager]`, `[InquirySelectOneByKeyEager]`
- DI registration through `AddInquiry()` + `AddInquirySqlServer(connectionString)`
- Eager loading on `Category.Products`
- Transactions through `IInquiry.BeginTransactionAsync()`

## Project layout

```
samples/Inquiry.Sample/
├── Program.cs                  one-time schema setup, DI wiring, Blazor pipeline
├── App.razor                   Blazor router root
├── _Imports.razor              shared @using directives
├── Data/
│   └── SampleDatabase.cs       CREATE TABLE statements
├── Models/                     entity classes (Organization, User, Product, …)
├── Stores/                     abstract partial stores annotated with Inquiry attributes
├── Services/                   one service per domain – the public API for pages
│   ├── DataSeeder.cs           idempotent first-run seed
│   ├── OrganizationService.cs  CRUD for organizations
│   ├── UserService.cs          CRUD + email lookup for users
│   ├── MembershipService.cs    org/user join queries, returns view-model rows
│   ├── CatalogService.cs       categories + eager-loaded products
│   └── TransactionDemoService.cs  multi-row insert inside a transaction
├── Pages/
│   ├── _Host.cshtml            host page for Blazor Server
│   ├── Index.razor             dashboard (`/`)
│   ├── Organizations.razor     `/organizations`
│   ├── Users.razor             `/users`
│   ├── Catalog.razor           `/catalog`
│   └── TransactionDemo.razor   `/transaction-demo`
├── Shared/
│   ├── MainLayout.razor        sidebar + content shell
│   └── NavMenu.razor           navigation links
└── wwwroot/
    └── css/site.css            page styling
```

## Why services, not stores, in the pages

Generated stores expose the raw CRUD surface — `SelectAllAsync`, `InsertAsync`, etc. — and
return `IAsyncEnumerable<T>`. The page layer wants list-shaped data and combined view
models (e.g., a membership row that already has its `Organization` and `User` resolved).

Each service in `Services/` hides that ergonomics gap: pages stay terse, and the
generated stores stay focused on raw data access. If you swap in a different storage
backend (or move stores to a different assembly), only the services change.

## Adding a page

1. Create a service in `Services/` that exposes the methods your page needs.
2. Register it in `Program.cs` with `AddScoped<MyService>()`.
3. Add a `.razor` file under `Pages/` with `@page "/route"` and `@inject MyService Svc`.
4. Add a `<NavLink>` to `Shared/NavMenu.razor`.
