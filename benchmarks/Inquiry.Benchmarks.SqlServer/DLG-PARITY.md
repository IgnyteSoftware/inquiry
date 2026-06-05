# DLG benchmark parity (SQL Server)

DLG is the legacy stored-procedure datalayer (`benchmarks/Inquiry.Benchmarks.DLG`). It is
SQL-Server-only, so its benchmark legs live in `Inquiry.Benchmarks.SqlServer` beside the ADO.NET,
Dapper, EF Core, and Inquiry legs. Run with Docker available and `--inProcess`.

## Supported — has a live DLG benchmark leg (Phase 1)

| Class | Category | DLG call |
|---|---|---|
| ShipperBenchmarks | SelectAll | `Shipper.SelectAllAsync()` |
| ShipperBenchmarks | SelectByKey | `Shipper.SelectOneAsync(id)` |
| ShipperBenchmarks | SelectByField | `Shipper.SelectByFieldAsync(ShipperFields.CompanyName, name)` |
| ShipperBenchmarks | Insert | `new Shipper{…}.InsertAsync()` |
| ShipperBenchmarks | Update | `new Shipper{…}.UpdateAsync()` (XML dirty-diff; key set + TakeSnapshot before field edits) |
| ShipperBenchmarks | Upsert | `new Shipper{…}.UpsertAsync()` |
| ProductReadBenchmarks | Count | `Product.SelectAllCountAsync()` |
| ProductReadBenchmarks | OffsetPage | `Product.SelectAllPagedAsync(pageNo, size, "ProductID")` |
| ProductReadBenchmarks | Search (LIKE) | `Product.SelectByFieldAsync(ProductFields.ProductName, "%x%", null, TypeOperation.Like)` |
| EagerLoadingBenchmarks | EagerParentChildren | `Category.SelectOneWithProductsUsingCategoryIDAsync(id)` |

## NotSupported — DLG has no first-class API

| Suite category | Why |
|---|---|
| Pagination — KeysetPage | DLG offers only offset paging (`SelectAllPaged`); no keyset/cursor. |
| Predicate — InList (`IN`) | `TypeOperation` is `{ Like, Less, Greater, Equal, NotEqual }` — no `IN`. |
| Aggregate — Sum / Avg / Min / Max | DLG exposes only `SelectAllCount`; no other aggregates. |
| Projection (subset columns → DTO) | DLG always materializes the full generated entity. |
| EagerLoading — all rows + parent (stitch) | DLG's only eager primitive is one-parent-with-children; the all-rows-with-parent shape is lazy per-row. |
| Batch — BatchInsert | DLG performs single-row Insert/Update/Upsert/Delete only. |

## N/A — not meaningful for DLG

| Suite category | Why |
|---|---|
| ParameterBinding (bind micro-benchmarks) | DLG binds only through stored-procedure parameters; no comparable low-level surface. |
| CrossDialectRead | DLG is single-dialect (SQL Server only). |

## Deferred to Phase 2 (DLG-capable, not yet wired)

Customer and Product full-CRUD classes on SQL Server (DLG supports the same CRUD surface as Shipper).
