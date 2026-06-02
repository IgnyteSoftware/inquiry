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

## Setup

Each benchmark class creates a fresh SQLite database in `%TEMP%`, applies the shared
`NorthwindSchema.SqliteDdl`, and seeds **1000** rows of each benchmarked entity (plus a
handful of categories so `Products.CategoryID` has valid FKs). The file is deleted on
`[GlobalCleanup]`.

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
