# Inquiry.Sample

A Blazor Server application that exercises Inquiry against a SQLite database mapped to the
classic Northwind schema. Schema, entities, and stores all come from the shared
[Inquiry.Northwind](../Inquiry.Northwind) project; the sample only owns the UI, the service
layer, and a small first-run seed.

## Running it

```powershell
dotnet run --project samples\Inquiry.Sample\Inquiry.Sample.csproj
```

The app reads `ConnectionStrings:InquirySample` from `appsettings.json` (default
`Data Source=northwind.db` — a file alongside the running process), runs
`NorthwindSchema.SqliteDdl` to create the tables if they do not exist, seeds a small
fixture via `DataSeeder`, then serves the Blazor UI at the URL printed in the console.

## What it demonstrates

- Entity mapping with `[InquiryTable]`, `[InquiryKey]`, `[InquiryColumn]`,
  `[InquiryForeignKey]` (Northwind entities live in `Inquiry.Northwind/Models`)
- Store generation with `[InquirySelectAll]`, `[InquirySelectOneByKey]`,
  `[InquirySelectAllByField]`, `[InquiryInsert]`, `[InquiryInsert(ReturnEntity = true)]`,
  `[InquiryUpdate]`, `[InquiryUpsert]`, `[InquiryDeleteOneByKey]`
- DI registration through `AddInquiry()` + `AddInquirySqlite(connectionString)`
- Transactions through `IInquiry.BeginTransactionAsync()`, combining a generated-store
  insert with raw-SQL inserts inside one commit
- Manual parent/child stitching in `CatalogService` as a workaround for a current
  generator limitation around nullable keys
  (see [Inquiry.Northwind/LIMITATIONS.md](../Inquiry.Northwind/LIMITATIONS.md))

## Project layout

```
samples/Inquiry.Sample/
├── Program.cs                       schema init, DI wiring, Blazor pipeline
├── App.razor                        Blazor router root
├── _Imports.razor                   shared @using directives
├── Services/                        one service per domain – the public API for pages
│   ├── CustomerService.cs           CRUD + country lookup for customers
│   ├── EmployeeService.cs           CRUD for employees, uses InsertReturning for IDENTITY
│   ├── CatalogService.cs            categories + products, in-memory stitch
│   ├── OrderTransactionService.cs   atomic Order + Order Details insert
│   └── DataSeeder.cs                idempotent first-run seed
├── Pages/
│   ├── _Host.cshtml                 host page for Blazor Server
│   ├── Index.razor                  dashboard (`/`)
│   ├── Customers.razor              `/customers`
│   ├── Employees.razor              `/employees`
│   ├── Catalog.razor                `/catalog`
│   └── TransactionDemo.razor        `/transaction-demo`
├── Shared/
│   ├── MainLayout.razor             sidebar + content shell
│   └── NavMenu.razor                navigation links
└── wwwroot/
    └── css/site.css                 page styling
```

## Why services, not stores, in the pages

Generated stores expose the raw CRUD surface — `SelectAllAsync`, `InsertAsync`, etc. — and
return `IAsyncEnumerable<T>`. The page layer wants list-shaped data and combined view
models (e.g., a catalog entry that bundles a category with its products). Each service in
`Services/` hides that ergonomics gap: pages stay terse, and the generated stores stay
focused on raw data access.
