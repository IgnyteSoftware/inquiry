# Inquiry.Benchmarks

BenchmarkDotNet harness comparing basic CRUD performance of Inquiry, EF Core, Dapper, and
raw ADO.NET. Two suites:

- **In-process (SQLite)** — `CustomerCrudBenchmarks` / `ProductCrudBenchmarks` / `ShipperCrudBenchmarks`.
  The definitive *library-overhead* comparison: with the database in-process, the per-call time and
  allocations reflect each library's binding/materialization cost (query start → return), not network or
  engine noise.
- **Cross-dialect (networked, Testcontainers)** — `CrossDialectReadBenchmarks`. **Inquiry vs Dapper** read
  hot-paths (SelectAll / SelectByKey) against PostgreSQL, MySQL, and SQL Server, provisioned via
  Testcontainers (`[Params]`-selected at runtime). Inquiry is exercised through its ad-hoc `IInquiry.Query…`
  path so all dialects share one assembly (the generated store fast-path is compile-time-per-dialect). On a
  networked engine the round-trip dominates, so the ad-hoc-vs-generated difference (a few µs) is negligible
  — making this a fair library comparison on real databases. EF Core is compared in the in-process SQLite
  suite (its quoted-identifier convention conflicts with the portable unquoted SQL on PostgreSQL). Requires
  Docker.

```powershell
# Cross-dialect read suite (PostgreSQL + MySQL + SQL Server, needs Docker)
dotnet run -c Release --project benchmarks\Inquiry.Benchmarks -- --filter "*CrossDialect*" --job short
```

## What it measures

### CRUD (`CustomerCrudBenchmarks` / `ProductCrudBenchmarks` / `ShipperCrudBenchmarks`)

For each of three entity shapes — `Customer` (string PK), `Product` (IDENTITY int? PK with
nullable columns), and `Shipper` (minimal IDENTITY) — the benchmark exercises six
operations:

| Operation     | Inquiry attribute used                    |
| ------------- | ----------------------------------------- |
| SelectAll     | `[InquirySelectAll]`                      |
| SelectByKey   | `[InquirySelectOneByKey]`                 |
| SelectByField | `[InquirySelectAllByField("...")]`        |
| Insert        | `[InquiryInsert]`                         |
| Update        | `[InquiryUpdate]`                         |
| Upsert        | `[InquiryUpsert]`                         |

EF Core, Dapper, and ADO.NET have no first-class upsert; the benchmark uses a hand-written
`INSERT … ON CONFLICT DO UPDATE` for each so the comparison is on wire SQL, not on
re-implementing the feature in client code.

### Feature benchmarks (Inquiry vs Dapper vs ADO.NET)

Beyond CRUD, these classes cover the higher-level store features. Each follows the same
conventions (`[MemoryDiagnoser]`, grouped by category, ADO.NET as the `[Baseline = true]`,
equal-column reads). EF Core is included only where it is a natural one-liner; where it is not
a like-for-like comparison it is omitted (noted in each class's doc comment).

| Class                            | Categories                | Measures                                                                                                   |
| -------------------------------- | ------------------------- | ---------------------------------------------------------------------------------------------------------- |
| `PaginationBenchmarks`           | OffsetPage, KeysetPage    | `LIMIT/OFFSET` vs keyset seek (`ProductID > @after`) at a deep page (offset ≈ `Rows / 2`).                 |
| `ProjectionAggregateBenchmarks`  | Projection, Count, Sum    | 3-column `ProductSummary` projection, `COUNT(*)`, and `SUM(UnitPrice)`.                                     |
| `PredicateBenchmarks`            | Search, InList            | Two-clause AND predicate (`UnitPrice >=` + `ProductName LIKE`) and `CategoryID IN (...)`.                   |
| `BatchBenchmarks`                | BatchInsert               | One batched multi-row INSERT (`[InquiryInsertAll]`) vs an N-row INSERT loop in a transaction.               |
| `EagerLoadingBenchmarks`         | EagerAll                  | Separate-query eager load of `Product.Category` vs a Dapper/ADO two-query-then-stitch of the same shape.    |

`ParameterBindingBenchmarks` (Inquiry's parameter-binding path in isolation, no SQL execution)
rounds out the suite.

## Dataset size (`[Params(1000, 100000)]`)

The read-oriented classes — both CRUD classes and `PaginationBenchmarks`,
`ProjectionAggregateBenchmarks`, `PredicateBenchmarks`, `EagerLoadingBenchmarks` — carry a
`[Params(1000, 100000)] public int Rows;` field, so BenchmarkDotNet runs every benchmark at
**two dataset tiers**: a small **1 000-row** set and a large **100 000-row** set. This shows
how each library's overhead scales: per-call fixed cost dominates at 1 000 rows, while
materialization / streaming cost dominates at 100 000. `BatchBenchmarks` is intentionally
**not** parameterized — it is a fixed-size (500-row) write benchmark, not a read over the
seeded data.

## Setup

Each benchmark class creates a fresh SQLite database in `%TEMP%`, applies the shared
`NorthwindSchema.SqliteDdl`, and seeds `Rows` rows of each benchmarked entity (plus a handful
of categories so `Products.CategoryID` has valid FKs). Seeding happens **once per parameter
value in `[GlobalSetup]`** — so the 100 000-row insert is paid a single time per class/tier,
outside the measured region, not on every invocation. The file is deleted on `[GlobalCleanup]`.

## Running

BenchmarkDotNet requires Release-mode binaries.

```powershell
# Full suite (all 3 entities × 6 operations × 4 libraries = 72 benchmarks)
dotnet run -c Release --project benchmarks\Inquiry.Benchmarks\Inquiry.Benchmarks.csproj

# Scope to one operation across all entities / libraries
dotnet run -c Release --project benchmarks\Inquiry.Benchmarks\Inquiry.Benchmarks.csproj -- --filter "*SelectAll*"

# Scope to one entity
dotnet run -c Release --project benchmarks\Inquiry.Benchmarks\Inquiry.Benchmarks.csproj -- --filter "*Customer*"

# Smoke-test wiring with a short job
dotnet run -c Release --project benchmarks\Inquiry.Benchmarks\Inquiry.Benchmarks.csproj -- --filter "*Customer*SelectByKey*" --job short
```

Results are written to `BenchmarkDotNet.Artifacts/` next to the run.

## Reading the results

Each benchmark class uses `[CategoriesColumn]` and groups by category, so the BDN summary
table prints one section per operation with all four implementations side-by-side. ADO.NET
is marked `[Baseline = true]` — the `Ratio` column shows each library's overhead relative
to hand-written ADO.NET.
