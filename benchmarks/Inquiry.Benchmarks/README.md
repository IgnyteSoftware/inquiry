# Inquiry.Benchmarks

BenchmarkDotNet harness comparing basic CRUD performance of Inquiry, EF Core, Dapper, and
raw ADO.NET. Two suites:

- **In-process (SQLite)** — `CustomerCrudBenchmarks` / `ProductCrudBenchmarks` / `ShipperCrudBenchmarks`.
  The definitive *library-overhead* comparison: with the database in-process, the per-call time and
  allocations reflect each library's binding/materialization cost (query start → return), not network or
  engine noise.
- **Cross-dialect (networked, Testcontainers)** — `CrossDialectReadBenchmarks`. All four libraries — raw
  ADO.NET (baseline), Dapper, EF Core, and Inquiry — on the read hot-paths (SelectAll / SelectByKey) against
  PostgreSQL, MySQL, and SQL Server, provisioned via Testcontainers (`[Params]`-selected at runtime). Inquiry
  is exercised through its ad-hoc `IInquiry.Query…` path so all dialects share one assembly (the generated
  store fast-path is compile-time-per-dialect). On a networked engine the round-trip dominates, so the
  ad-hoc-vs-generated difference (a few µs) is negligible — making this a fair library comparison on real
  databases. All identifiers are **all-lowercase** so the one physical `shippers` table is addressable
  identically by EF Core (which quotes identifiers), the portable-unquoted libraries, and each engine's
  folding/casing rules — which is what lets EF Core join the cross-dialect comparison. Requires Docker.
- **Provider-specific (networked, Testcontainers)** — provider projects under
  `benchmarks/Inquiry.Benchmarks.{PostgreSql,MySql,Oracle,SqlServer}` compile generated stores for one
  dialect at a time. PostgreSQL also includes `PreparedStatementBenchmarks`, which compares Inquiry
  `PreparedStatementMode.None` vs `Auto` on Npgsql for a generated simple point read and a stable ad-hoc
  multi-join point read.

```powershell
# Cross-dialect read suite (PostgreSQL + MySQL + SQL Server, needs Docker).
# NOTE: --job short is a fast wiring/smoke run with wide error bars. Use the default job for
# any numbers you intend to publish or compare — short-job noise can invert sub-microsecond
# orderings (and is what made wrappers appear to "beat" the ADO.NET baseline in an earlier run).
dotnet run -c Release --project benchmarks\Inquiry.Benchmarks -- --filter "*CrossDialect*" --job short
```

```powershell
# PostgreSQL prepared-statement comparison (needs Docker).
# Use --job short only as a smoke run; use the default BenchmarkDotNet job before publishing numbers.
dotnet run -c Release --project benchmarks\Inquiry.Benchmarks.PostgreSql\Inquiry.Benchmarks.PostgreSql.csproj -- --filter "*PreparedStatementBenchmarks*" --job short --inProcess
```

## Fairness (apples-to-apples)

Inquiry, Dapper, and EF Core are all built **on top of** ADO.NET, so the raw-ADO.NET
`[Baseline = true]` must be a **true floor**: it has to perform the *identical* ADO.NET work
each library does internally. Otherwise a wrapper can print a sub-1.00× `Ratio` — appearing
faster than the ADO.NET it is layered on, which is impossible for honest overhead and means
the **baseline** is wrong, not the wrapper.

Two invariants keep the comparison honest:

- **Matching `CommandBehavior`.** Inquiry's generated stores open readers with
  `CommandBehavior.SingleResult | SequentialAccess` for list reads and
  `SingleResult | SingleRow | SequentialAccess` for single-row reads. `SequentialAccess` lets the
  provider stream each row forward-only instead of buffering it (Dapper passes equivalent flags) —
  this roughly halves allocation on large/wide reads, so without it the baseline would buffer while
  the wrappers stream and a wrapper would print a sub-1.00× allocation ratio (as SQL Server
  `SelectAll` did before this floor was applied). Every ADO.NET baseline reader passes the **same**
  flags and reads columns in ascending ordinal order, so it is never handicapped relative to the
  wrappers it floors.
- **Matching connection lifecycle.** ADO.NET, Dapper, and Inquiry each open a fresh connection
  per call (pooled underneath by the provider). EF Core therefore uses a **non-pooled**
  `DbContextFactory` (`AddDbContextFactory`, *not* `AddPooledDbContextFactory`) so it pays
  per-operation context construction instead of reusing warm context state the other three legs
  never get.

**Gate:** in the in-process SQLite suite, no non-baseline leg should print a `Ratio` below
~1.00 (modulo noise). A wrapper under 1.00 means the baseline has drifted out of parity — fix
the baseline, not the wrapper.

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
| `PreparedStatementBenchmarks`    | SimplePointRead, MultiJoinPointRead | PostgreSQL-only comparison of Inquiry `PreparedStatementMode.None` vs `Auto` on generated and ad-hoc stable SQL. |

### Generated-command hot path (`ParameterBindingBenchmarks` / `GeneratedCommandPipelineBenchmarks`)

`ParameterBindingBenchmarks` is the retained-command floor. It isolates generated-store dispatch and
parameter binding from database execution, calling real source-generated `[InquiryExists]` store
methods with four state shapes:

- parameterless;
- one scalar parameter;
- eight scalar parameters, which forces C#'s nested `ValueTuple` lowering; and
- one collection predicate using SQLite's generated JSON-array transport.

The generated legs dispatch an `InquiryGeneratedCommand<TArgs>` to a benchmark `IInquiry` sink. The
sink reuses one provider `DbCommand`, applies the generated static binder, and never opens a
connection. Every boxed `InquiryCommand` overload throws, so the benchmark fails immediately if a
generator change falls back to the allocating compatibility path. Each generated leg is compared
with a direct static-binder floor that performs the same command reset, command-text assignment, and
provider-parameter creation. `[MemoryDiagnoser]` reports managed allocation and
`[DisassemblyDiagnoser]` writes JIT assembly reports for auditing closure, delegate, and tuple costs.

`GeneratedCommandPipelineBenchmarks` is intentionally a separate end-to-end measurement. Its
parameterless, one-parameter, and eight-parameter methods execute real SQL against shared in-memory
SQLite through this complete route:

`generated store -> DefaultInquiry -> built-in InquiryRequestPipeline -> SQLite`

Setup separately invokes the same methods through a routing guard that forwards
`InquiryGeneratedCommand<TArgs>` and throws immediately if generated code reaches a boxed
`InquiryCommand` scalar overload; the guard is not present in measured operations. These measurements
include connection open/close, provider command creation and disposal, parameter binding, SQLite
execution, scalar conversion, and task completion. They are not binder-only allocation floors and
should not be compared as if they were. Each parameter shape compares the no-interceptor baseline
with registered-but-inactive telemetry and a minimal active custom interceptor. Setup executes and
validates every SQL shape and verifies that the custom interceptor receives all three operations.

```powershell
# Retained-command binder/JIT floor. Omit --job Dry for publishable measurements and disassembly.
dotnet run -c Release --framework net10.0 -r win-x64 --project benchmarks\Inquiry.Benchmarks\Inquiry.Benchmarks.csproj -- --filter "*ParameterBindingBenchmarks*" --job Dry

# Real generated-store -> DefaultInquiry -> built-in-pipeline execution and allocation smoke.
dotnet run -c Release --framework net10.0 -r win-x64 --project benchmarks\Inquiry.Benchmarks\Inquiry.Benchmarks.csproj -- --filter "*GeneratedCommandPipelineBenchmarks*" --job Dry
```

The explicit Windows RID overrides the benchmark project's Linux CI default; omit it when running on
Linux. As with the other suites, a Dry result proves wiring but is not statistically meaningful.

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

## SQL Server collection transports

`SqlServerCollectionBenchmarks` compares a pre-provisioned generated TVP with typed `OPENJSON`
and scalar parameter expansion for 1, 10, 100, and 1,000 product IDs. Every operation uses the
same buffered ten-column projection, a fresh pooled connection, one command, and the standard
10,000-product fixture. Setup streams all 13 canonical Northwind tables into a fresh database using
bounded `SqlBulkCopy`, applies full-scan statistics, and validates the generated TVP artifact before
any collection starts.

The checked jobs below are the authoritative collection commands. They are intentionally long-running
and require Docker; results are evidence, not a claim that any transport always wins.

The SQL Server benchmark project pins a Linux RID for release evidence. On Windows, build normally and
invoke the managed DLL instead of `dotnet run` (the latter attempts to launch the Linux apphost):

```powershell
dotnet build benchmarks/Inquiry.Benchmarks.SqlServer/Inquiry.Benchmarks.SqlServer.csproj -c Release -f net8.0
dotnet benchmarks/Inquiry.Benchmarks.SqlServer/bin/Release/net8.0/linux-x64/Inquiry.Benchmarks.SqlServer.dll --collection-smoke
```

Use that managed-DLL form with `--collection-benchmark`, `--collection-verify`, or
`--collection-evidence <path>` for the corresponding Windows command below.

```powershell
dotnet run -c Release -f net8.0 --project benchmarks/Inquiry.Benchmarks.SqlServer -- --collection-benchmark
dotnet run -c Release -f net10.0 --project benchmarks/Inquiry.Benchmarks.SqlServer -- --collection-benchmark
```

The smoke commands only validate wiring and are not suitable for published comparisons:

```powershell
dotnet run -c Release -f net8.0 --project benchmarks/Inquiry.Benchmarks.SqlServer -- --collection-smoke
dotnet run -c Release -f net10.0 --project benchmarks/Inquiry.Benchmarks.SqlServer -- --collection-smoke
```

Correctness and server evidence run outside the timed BenchmarkDotNet path. Evidence contains logical
reads and hashed SQL/parameter/query/plan signatures; it intentionally contains no SQL text, raw rows,
hosts, secrets, or local paths.

The old `PlanCacheBenchmarks` suite was removed in full because it timed cache clearing and therefore
could not produce authoritative latency results. Its declared-size versus inferred-size experiment was
not silently carried forward; a separately controlled parameter-signature experiment remains explicit
follow-up work under #87.

```powershell
dotnet run -c Release -f net8.0 --project benchmarks/Inquiry.Benchmarks.SqlServer -- --collection-verify
dotnet run -c Release -f net8.0 --project benchmarks/Inquiry.Benchmarks.SqlServer -- --collection-evidence artifacts/benchmarks/sqlserver-collection-evidence.json
```

Until checked historical baselines and a stable benchmark runner are added under the broader benchmark
roadmap, local output and collected server evidence are non-release-gating and non-authoritative.

## Reading the results

Each benchmark class uses `[CategoriesColumn]` and groups by category, so the BDN summary
table prints one section per operation with all four implementations side-by-side. ADO.NET
is marked `[Baseline = true]` — the `Ratio` column shows each library's overhead relative
to hand-written ADO.NET.
