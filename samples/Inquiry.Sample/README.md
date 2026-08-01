# Inquiry.Sample

A Blazor Server application that exercises Inquiry against a Northwind-shaped database on
SQLite, SQL Server, or PostgreSQL — picked at runtime from configuration. Schema, entities,
and stores all come from the shared [Inquiry.Northwind](../Inquiry.Northwind) project; the
sample only owns the UI, the service layer, and a small first-run seed.

## Running it

```powershell
dotnet run --project samples\Inquiry.Sample\Inquiry.Sample.csproj
```

On startup `InquiryProviderSetup` reads `Inquiry:Provider` from `appsettings.json`, picks
the matching connection string under `ConnectionStrings`, ensures the target database
exists, runs the provider's Northwind DDL, then registers the matching Inquiry services.
`DataSeeder` then drops in a small fixture (no-ops if customers already exist) and the
Blazor UI starts at the URL printed in the console.

## Choosing a provider

Set `Inquiry:Provider` to one of `Sqlite` (default), `SqlServer`, or `PostgreSql`. Each
provider reads its own connection string under `ConnectionStrings`:

| Provider | Config value | Connection string key | Default in `appsettings.json` |
| --- | --- | --- | --- |
| SQLite     | `Sqlite`     | `InquirySample.Sqlite`     | `Data Source=northwind.db` (file alongside the process) |
| SQL Server | `SqlServer`  | `InquirySample.SqlServer`  | LocalDB pointing at `InquirySample` |
| PostgreSQL | `PostgreSql` | `InquirySample.PostgreSql` | `localhost` / `inquirysample` / `postgres` superuser |

The connection strings in `appsettings.json` are **local-development defaults only** — they
are not real credentials. To point the sample at a real database, set the **`INQUIRY_SAMPLE_DB`**
environment variable: it overrides the active provider's connection string (taking precedence
over `appsettings.json`), so you never have to commit credentials. Pick the provider with
`Inquiry__Provider`:

```powershell
$env:Inquiry__Provider = "PostgreSql"
$env:INQUIRY_SAMPLE_DB  = 'Host=db.internal;Database=inquirysample;Username=app;Password=<your-password>'
dotnet run --project samples\Inquiry.Sample\Inquiry.Sample.csproj
```

For SQL Server and PostgreSQL, the sample creates the target database if it doesn't
already exist (using a `master` / `postgres` bootstrap connection with the same
credentials), so a default install just works.

## What it demonstrates

The sample exercises every classic Northwind table — including the three composite-key
ones (`Order Details`, `EmployeeTerritories`, `CustomerCustomerDemo`) — through Inquiry's
generated stores. Specifically:

- Entity mapping with `[InquiryTable]`, `[InquiryKey]` (single and composite),
  `[InquiryColumn]`, `[InquiryForeignKey]`, `[InquiryRelation]`
  (Northwind entities live in `Inquiry.Northwind/Models`)
- Store generation with `[InquirySelectAll]`, `[InquirySelectOneByKey]`,
  `[InquirySelectAllByField]` (single- and multi-column),
  `[InquirySelectAllEager]`, `[InquirySelectOneByKeyEager]`,
  `[InquiryInsert]`, `[InquiryInsert(ReturnEntity = true)]`,
  `[InquiryUpdate]`, `[InquiryUpdate(ReturnEntity = true)]`,
  `[InquiryUpsert]`, `[InquiryUpsert(ReturnEntity = true)]`,
  `[InquiryDeleteOneByKey]`
- Eager loading on both Region↔Territory (non-nullable key) and Category↔Product
  (nullable IDENTITY key)
- DI registration through `AddInquiry()` + `AddInquiryGeneratedStores()` +
  `AddInquirySqlite(...)` / `AddInquirySqlServer(...)` / `AddInquiryPostgreSql(...)`
- Ambient transactions through `IInquiry.BeginTransactionAsync()`, combining an
  IDENTITY-keyed parent insert (`Orders` via `InsertReturning`) with composite-key child
  inserts (`Order Details`) inside one commit

## Project layout

```
samples/Inquiry.Sample/
├── Program.cs                       schema init, DI wiring, Blazor pipeline
├── App.razor                        Blazor router root
├── _Imports.razor                   shared @using directives
├── Services/                        one service per domain – the public API for pages
│   ├── CustomerService.cs           CRUD + country lookup for customers
│   ├── EmployeeService.cs           CRUD for employees, uses InsertReturning for IDENTITY
│   ├── CatalogService.cs            categories + products via eager loading
│   ├── SupplierService.cs           CRUD for suppliers
│   ├── ShipperService.cs            CRUD for shippers
│   ├── RegionService.cs             regions + territories via eager loading
│   ├── OrderService.cs              orders + composite-key order details
│   ├── DemographicsService.cs       customer demographics + composite-key bridge
│   ├── EmployeeTerritoryService.cs  int+string composite bridge
│   ├── OrderTransactionService.cs   atomic Order + Order Details insert
│   └── DataSeeder.cs                idempotent first-run seed (all 13 tables)
├── Pages/
│   ├── _Host.cshtml                 host page for Blazor Server
│   ├── Index.razor                  dashboard (`/`)
│   ├── Customers.razor              `/customers`
│   ├── Employees.razor              `/employees`
│   ├── Catalog.razor                `/catalog`
│   ├── Suppliers.razor              `/suppliers`
│   ├── Shippers.razor               `/shippers`
│   ├── Regions.razor                `/regions`
│   ├── Orders.razor                 `/orders`
│   ├── Demographics.razor           `/demographics`
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
