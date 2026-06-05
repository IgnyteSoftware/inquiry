# DLG benchmark integration — design (Phase 1, trimmed)

**Date:** 2026-06-04
**Status:** Approved direction (trimmed-first); pending spec review
**Author:** Jake Overstreet + Claude

## Problem

`benchmarks/Inquiry.Benchmarks.DLG` is a newly-added, SQL-Server-only, stored-procedure-based
datalayer ("DLG 6.0.1" generator output) covering the Northwind schema. We want it as a fourth/fifth
"leg" in our benchmark suite so we can measure the new Inquiry framework against this older DLG
engine. DLG must be benchmarked **only against SQL Server** (it supports no other dialect).

Each DLG table maps to four classes the end user touches — `Table` (e.g. `Shipper`), `Tables`
(`Shippers`), `TablePrimaryKey` (`ShipperPrimaryKey`), and the generated `TableBase`
(`ShipperBase`, holding the SP-backed methods). A `DatabaseHelper` carries the connection; passing
`null` makes it self-load from `Inquiry.Benchmarks.DLG.config` via the internal, statically-cached
`ConfigurationHelper`.

## Goals (Phase 1)

1. Make `Inquiry.Benchmarks.DLG` build and be referenceable from the net8 benchmark host.
2. Run DLG inside the existing `Inquiry.Benchmarks.SqlServer` Testcontainer process, wired through
   DLG's own `.config` mechanism (the user's chosen approach).
3. Add a DLG leg to the existing **Shipper CRUD** comparison and to the **supported read extras**
   (offset pagination, LIKE search, count, eager parent-with-children).
4. Produce a **feature-support recap** marking everything DLG cannot do as `NotSupported`.

## Non-goals (Phase 1 — deferred to Phase 2)

- Customer/Product full CRUD classes on SQL Server.
- Categories DLG cannot serve, kept in the recap only: KeysetPage, InList (`IN`),
  Sum/Avg/Min/Max, Projection, BatchInsert, ParameterBinding micro, CrossDialectRead.
- Any change to the Inquiry framework, the core SQLite benchmarks, or other dialect projects.
- Any edit to DLG's generated `.cs` files (only its `.csproj` changes).

## Architecture

### Placement & build

- **Home:** DLG benchmarks live in `benchmarks/Inquiry.Benchmarks.SqlServer` — the only project that
  benchmarks a real SQL Server (Testcontainers, `mcr.microsoft.com/mssql/server:2022-latest`). This
  puts the DLG leg in the same process and container as the ADO.NET / Dapper / EF Core / Inquiry
  legs, for an apples-to-apples comparison.
- **TFM:** retarget `Inquiry.Benchmarks.DLG` from `net10.0` to **multi-target `net8.0;net10.0`**.
  The generated code uses no net10-only APIs; multi-targeting keeps standalone net10 use while
  letting the net8 host reference the net8 asset. (Fallback: net8.0-only if multi-target surfaces a
  compile issue.)
- **Packages (Central Package Management):** DLG's csproj currently has zero `PackageReference`s and
  will not compile. Add `PackageReference`s (no version) plus matching `PackageVersion` entries in
  `Directory.Packages.props` for: `Microsoft.Data.SqlClient` (already cataloged),
  `Microsoft.Extensions.Configuration`, `Microsoft.Extensions.Configuration.Json`,
  `System.Configuration.ConfigurationManager`, `System.Data.OleDb`, `System.Data.Odbc`. Versions
  aligned with the existing 8.x line; finalized against the build.
- **Solution:** add `Inquiry.Benchmarks.DLG` to `Inquiry.slnx` under `/benchmarks/`.
- **Reference & aliasing:** `Inquiry.Benchmarks.SqlServer` adds a `ProjectReference` to
  `Inquiry.Benchmarks.DLG`. DLG's `Shipper`/`Product`/`Category` type names collide with the Inquiry
  Northwind models already in scope, so DLG types are reached via an alias:
  `using Dlg = Inquiry.Benchmarks.DLG;` → `Dlg.Shipper`, `Dlg.Product`, `Dlg.Category`.

### Database & config wiring ("Testcontainers + .config")

All added once in `SqlServerBenchmarkDatabase.CreateAsync`, inside the existing `_container is null`
guard (process-wide, runs in `[GlobalSetup]` before any benchmark method, satisfying DLG's
read-once static config cache):

1. **Existing:** start container → run `NorthwindSchema.SqlServerDdl` (creates all 13 Northwind
   tables, IDENTITY keys).
2. **New — apply DLG procs:** read `SQLScript.sql` (linked from the DLG project into the SqlServer
   benchmark as content/embedded resource), split into batches on lines equal to `GO`, execute each
   batch with `ExecuteNonQuery`. Creates the `gsp_*` procedures over the existing tables.
3. **New — prime DLG config:** write `Inquiry.Benchmarks.DLG.config` to `AppContext.BaseDirectory`
   (where `ConfigurationHelper` looks) containing the container's connection string and
   `providerName="Microsoft.Data.SqlClient"`. **Note:** the shipped `.config` uses
   `providerName="System.Data.SqlClient"`, which DLG's `GetSupportedDatabaseFromProviderName` does
   not recognize and would throw on — the primed file must use `Microsoft.Data.SqlClient`.

DLG is then exercised as intended — `Dlg.Shipper.SelectAllAsync()`, `new Dlg.Shipper{…}.InsertAsync()`
with `databaseHelper = null` — self-loading the primed config and opening a fresh pooled connection
per call, the same lifecycle as the other legs.

Constraints (already true for this project): requires Docker and BenchmarkDotNet `--inProcess`.

### Seeding

Current seed fills only `Shippers`. Phase 1 anchors the read extras on **Products** (with parent
**Categories**), so extend `SqlServerBenchmarkDatabase.SeedAsync` to also seed `Categories` and
`Products` (Products carrying valid `CategoryID` FKs), once, at the existing `[Params(1000)]` scale.
Seeding mirrors the core `BenchmarkDatabase` approach. No Orders/Order Details needed in Phase 1.

## Benchmark set (Phase 1)

Each operation runs the standard legs — ADO.NET (baseline), Dapper, EF Core, Inquiry — **plus DLG**.
EF is included where the EF context maps the entity; for the eager case EF may be omitted following
the existing `EagerLoadingBenchmarks` precedent (its `Include` is not a like-for-like match). The EF
SQL Server context is extended minimally with `Product`/`Category` sets where an EF leg is kept.

1. **`ShipperBenchmarks` (edit existing):** add a `*_Dlg` leg to each category —
   `SelectAll`, `SelectByKey`, `SelectByField`, `Insert`, `Update`, `Upsert`.
   DLG calls: `Dlg.Shipper.SelectAllAsync()`, `Dlg.Shipper.SelectOneAsync(id)`,
   `Dlg.Shipper.SelectByFieldAsync(ShipperFields.CompanyName, name)`,
   `new Dlg.Shipper{…}.InsertAsync()`, `…UpdateAsync()`, `…UpsertAsync()`.
2. **New read-extras class(es)** anchored on Products/Categories:
   - **Count** — `Dlg.Product.SelectAllCountAsync()`.
   - **OffsetPage** — `Dlg.Product.SelectAllPagedAsync(pageNumber, pageSize, "ProductID")`.
   - **Search (LIKE)** — `Dlg.Product.SelectByFieldAsync(ProductFields.ProductName, "%a%", null, TypeOperation.Like)`.
   - **EagerParentChildren** — `Dlg.Category.SelectOneWithProductsUsingCategoryIDAsync(categoryId)`
     (one parent + its Products in one round-trip); other legs do the equivalent parent+children load.

Each DLG benchmark returns the same shape (row count / entity) the other legs return, so results are
verified equal before timings are trusted.

## Feature-support recap

Shipped as `benchmarks/Inquiry.Benchmarks.SqlServer/DLG-PARITY.md` and summarized here. Phase-1
"Supported" items have a live DLG benchmark leg; the rest are documented.

| Category | Operation | DLG | Notes |
|---|---|---|---|
| CRUD | SelectAll / SelectByKey / SelectByField | ✅ | generated per-entity SP methods |
| CRUD | Insert / Update / Upsert | ✅ | Update = XML dirty-diff of changed columns |
| Pagination | OffsetPage | ✅ | `SelectAllPagedAsync` |
| Pagination | KeysetPage | ❌ NotSupported | no keyset API |
| Predicate | Search (LIKE) | ✅ | `TypeOperation.Like` |
| Predicate | InList (IN) | ❌ NotSupported | `TypeOperation` has no `In` |
| Aggregate | Count | ✅ | `SelectAllCountAsync` |
| Aggregate | Sum / Avg / Min / Max | ❌ NotSupported | no aggregate API beyond Count |
| Projection | subset columns → DTO | ❌ NotSupported | always materializes the full entity |
| EagerLoading | one parent + children | ✅ | `SelectOneWith<Child>Async` |
| EagerLoading | all rows + parent (stitch) | ❌ NotSupported | only lazy per-row navigation |
| Batch | BatchInsert | ❌ NotSupported | single-row operations only |
| ParameterBinding | bind micro-benchmarks | ❌ N/A | binds only through SP parameters |
| CrossDialectRead | SelectAll / SelectByKey | ❌ N/A | single-dialect (SQL Server only) |

## Risks & verification

- **Proc/DDL type fit** — DLG procs were generated against classic Northwind (`text`/`image`); the
  repo DDL uses `NVARCHAR(MAX)`/`VARBINARY(MAX)`. Implicit conversions should hold. **Gate:** apply
  all procs, then smoke-test one DLG call per touched entity (Shipper, Product, Category) during
  setup before relying on any timing. Reconcile minimally if a proc rejects.
- **IDENTITY** — verified: `gsp_Shippers_Insert` omits the IDENTITY column, compatible with the
  `IDENTITY(1,1)` DDL.
- **Update dirty-tracking** — DLG snapshots in its constructor then sends only changed columns;
  verify the keyed Update/Upsert mutates exactly the target row.
- **Static config cache** — only the first connection string loaded survives for the process; safe
  given one shared container per process. The `.config` must be primed before the first DLG call.
- **Windows-only bits** — DLG uses `DllImport("Rpcrt4.dll")` (sequential GUID) and OleDb/Odbc; not
  exercised by the int/string-keyed Phase-1 entities and the benchmark host is Windows.

**Success criteria:** the SqlServer benchmark project builds with the DLG reference; a filtered run
(e.g. `--filter *Shipper*` and the read-extras) completes with a DLG leg reporting alongside the
others and correct row counts; `DLG-PARITY.md` documents the matrix above.

## Open implementation decisions (with defaults)

- **Read-extras class layout** — one consolidated `DlgReadBenchmarks` vs per-category classes
  mirroring the core suite. *Default:* per-category classes (`PaginationBenchmarks`,
  `PredicateBenchmarks`, `AggregateBenchmarks`, `EagerLoadingBenchmarks`) scoped to Phase-1
  operations, matching the suite's one-class-per-concern style.
- **EF leg for extras** — extend the SqlServer EF context with Product/Category vs omit EF.
  *Default:* include EF for Count/OffsetPage/Search (small context extension); omit EF for the eager
  case per the existing `EagerLoadingBenchmarks` precedent.
- **DLG package versions** — pin against the build; align to the existing 8.x line.
