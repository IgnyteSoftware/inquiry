# Inquiry product workspace

A runnable Blazor Server application backed by SQLite and generated Inquiry stores. The main
screen combines product search, filtering, sorting, paging, creation, editing, concurrency
conflicts, soft deletion, and two separate workspaces. The original Northwind pages remain
available for relationships, composite keys, and order transactions.

## Run

Install the .NET 10 SDK, then run from the repository root:

```powershell
dotnet run --project samples/Inquiry.Sample/Inquiry.Sample.csproj --no-launch-profile --urls http://localhost:5188
```

Open http://localhost:5188. Startup creates the SQLite schema and seeds 24 products in each
workspace. No database server or credentials are required. The default database is
`northwind.db` in the application's working directory. Data persists between runs.

For a separate dataset, provide a new SQLite file path (its parent directory must exist):

```powershell
$env:INQUIRY_SAMPLE_DB = 'Data Source=D:/scratch/inquiry-demo.db'
dotnet run --project samples/Inquiry.Sample/Inquiry.Sample.csproj --no-launch-profile --urls http://localhost:5188
```

The sample is for local exploration. The workspace selector demonstrates data scoping;
it does not authenticate users or authorize access. SQLite is the supported sample provider:
the data libraries compile SQLite SQL, so swapping a runtime connection provider cannot make
them target another dialect. Non-SQLite configuration fails at startup with an explanation.

## Walk through it

1. Search for `tea`: the initial Northwind dataset returns Matcha tea and Tea strainer.
2. Reset, select Unassigned, and search: four initial products have no category.
3. Combine a price range and In stock only; try all four sorts and move through pages.
4. Add a product. Try saving without a name, then supply valid values and save.
5. Change its stock and save again. Each save uses the returned entity and refreshed token.
6. Open that product in another tab. Save an edit in the first tab, then save the stale second
   copy. The second save reports a conflict; Reload product retrieves the current version.
7. Archive a product and confirm the action. Search results and totals exclude it, while
   its row remains in SQLite. Saving an older copy cannot restore it through this service.
8. Switch to Market House. Its search results, counts, and editable products are separate.
9. Visit Developer guide, Catalog, Orders, and Transaction Demo for more examples.

A search with no matches shows an empty state and Clear filters. Invalid price ranges produce
validation messages. Reset clears filters, and applying filters returns to the first page.
An archive on the final page moves back to the last remaining page when necessary.

## Follow the source

| File | Purpose |
| --- | --- |
| [Products.razor](Pages/Products.razor) | Search form, paged results, editor, conflicts, archive confirmation |
| [ProductWorkspace.cs](../Inquiry.Sample.Data/ProductWorkspace.cs) | Application operations, validation, scoped filters, reports, transactional seed |
| [WorkspaceStores.cs](../Inquiry.Sample.Data/WorkspaceStores.cs) | Generated paged searches, inserts, updates, and soft deletes |
| [WorkspaceProduct.cs](../Inquiry.Sample.Data/WorkspaceProduct.cs) | Mappings, nullable category, tenant field, concurrency token, report DTO |
| [WorkspaceRegistration.cs](../Inquiry.Sample.Data/WorkspaceRegistration.cs) | Library-owned store registration and generated schema |
| [Program.cs](Program.cs) | Startup, registration, seeding, Blazor/static assets |
| [Showcase.razor](Pages/Showcase.razor) | In-app developer guide |
| [Northwind models and stores](../Inquiry.Northwind) | Shared Northwind entities, relationships, composite keys |
| [Services](Services) | Existing Northwind workflows, including order transactions |

The UI calls one application service. It handles workspace scope, validation, and composed
operations; stores remain the generated database boundary. Example:

```csharp
var page = await workspace.SearchAsync(1, new ProductSearch(
    Name: "tea", Minimum: 10m, InStock: true,
    Sort: ProductSort.Price, Offset: 0, Limit: 10), cancellationToken);

var product = await workspace.FindAsync(1, productId, cancellationToken);
if (product is not null)
{
    product.Stock += 5;
    var saved = await workspace.SaveAsync(1, product, cancellationToken);
    // A null result means the row changed or is no longer active.
}
```

## What this application tells us about the API

- Four sorts plus a distinct Unassigned filter require eight generated search declarations.
  This is deliberately visible in the sample and motivates issues #425 and #426.
- Parameterized predicate queries return whole mapped entities. Categories are buffered
  separately because ordinary eager loading does not accept these search predicates (#401, #415).
- Raw SQL reports explicitly include workspace and soft-delete predicates; generated filters
  do not apply to handwritten SQL. The ad-hoc DTO follows SELECT ordinal order (#420, #427).
- Each data library owns a registration wrapper to avoid identical generated extension method
  names when an application references multiple libraries (#382).
- Generated updates and archives atomically check active state and the expected version.
  Archiving advances the concurrency token. The service also illustrates an active-state and
  version pre-check inside the same SQLite transaction. Stale editors cannot restore archived
  rows or report a repeated archive as successful.
- Name search uses SQL LIKE: `%` matches any sequence and `_` matches one character. Ordinary
  text becomes a contains pattern. Values are passed as parameters.
- Stored procedures need server-provider examples and integration verification. SQLite has
  none, and this sample does not pretend to demonstrate them (#423).

Schema creation here is initial setup, not a migration system. Existing databases are not
upgraded when mappings change. Choose a new database file when exploring schema changes.
Set `Logging:LogLevel:Inquiry.Command` to `Debug` to inspect generated SQL locally.

Track the sample and verification evidence in [#428](https://github.com/IgnyteSoftware/inquiry/issues/428).
