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
  multi-join point read. SQL Server also includes `GeneratedAdHocSequentialAccessBenchmarks`, which
  isolates generated ad-hoc interface dispatch, provider buffering, and partial stream consumption.

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
  `SingleResult | SequentialAccess` for validating single-row reads. Generator-proven unique reads
  additionally use `SingleRow`. `SequentialAccess` lets the
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

**Exempt: the grid-eager classes** (`EagerGridBenchmarks`, `EagerGridMixedRelationBenchmarks`). There the
ADO.NET baseline deliberately runs *separate* queries and stitches in memory, while Inquiry issues a
single multi-result-set command — so a `Ratio` or `Alloc Ratio` below 1.00 is the architectural
difference being measured, not baseline drift. Do not "fix" it.

Ratios in the committed `EagerGridBenchmarks` baseline (`Grid_Inquiry` ÷ `Grid_AdoNet`, median): 0.97 at
(1000, 4), 1.00 at (1000, 100), **1.15 at (100000, 4)**, 0.98 at (100000, 100). That dense-100k outlier
predates the children-first streaming change (#70) and has not been re-measured since. It is tracked as
[#265](https://github.com/IgnyteSoftware/inquiry/issues/265) — because the baseline pins 1.15 as the
expected value, the regression gate will never flag it. `EagerLoadingBenchmarks` is **not** exempt — it
runs 1.11–1.13× and the ordinary rule applies.

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
| `BatchBenchmarks`                | BatchInsert               | One batched multi-row INSERT (`[InquiryInsert]`) vs an N-row INSERT loop in a transaction.               |
| `EagerLoadingBenchmarks`         | EagerAll                  | Eager load of the `Product.Category` reference vs a Dapper/ADO two-query-then-stitch of the same shape.     |
| `EagerGridBenchmarks`            | EagerGrid                 | `Region → Territories` collection, one grid command vs two queries stitched. Density: `RegionCount` 4 (dense) or 100 (sparse). |
| `EagerGridMixedRelationBenchmarks` | EagerGridMixed          | 1 parent + 2 relations (`MixedBenchPost` → to-one `Author`, to-many `Tags`), one grid command vs three queries stitched. `Rows` is the **tag** count; `PostCount` 4 (dense) or 100 (sparse). Seeds only its own tables (`CreateAsync(seedRows: 0)`), unlike the classes above. |
| `PreparedStatementBenchmarks`    | SimplePointRead, MultiJoinPointRead | PostgreSQL-only comparison of Inquiry `PreparedStatementMode.None` vs `Auto` on generated and ad-hoc stable SQL. |

#### Why there is no working-set column

Peak working set is deliberately **not** reported. It was built and measured three ways during #70, and
none of them tracked the workload:

| Measured as | Result |
| --- | --- |
| Absolute process peak | 59% swing between identical runs (215,800 vs 342,844 KB) while `Allocated` stayed byte-stable; leg ordering inverted |
| Delta from `BeforeActualRun` | That signal fires *after* warmup, which has already run the workload and grown the heap — so the delta is residual growth, not footprint |
| Delta from `BeforeAnythingElse` | Back to GC-dominated: 200,380 / 462,648 / 195,848 KB for operations allocating 92 / 156 / 91 KB, with ordering still inverting |

The cause is `ServerGarbageCollection` (on for these projects): working set reflects the GC's reservation
policy, which scales with core count and available RAM, not with what the benchmark allocates. No reset
point fixes that. **`Allocated` is the actionable memory signal** — it reproduces to the byte and is
gated by the #87 regression budget.

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

### Generated ad-hoc sequential access (`GeneratedAdHocSequentialAccessBenchmarks`)

The SQL Server provider suite seeds 32 identically shaped wide rows: twelve ordered scalar columns followed
by one 64 KiB `VARBINARY(MAX)` payload. Every measured SQL leg executes the same ordered `SELECT`;
every fully-consumed leg materializes and checksums every scalar and every payload byte.

- `MaterializerDispatch` invokes the generated class through a cached
  `IInquiryEntityMaterializer<T>` and the generated struct through its constrained generic call over
  the same in-memory `DataTableReader` row. It excludes DI, network, and provider I/O so the generated
  materializer dispatch difference is visible in timing and disassembly.
- `EndToEndAdHocPath` compares the public generated class/DI path with the generated struct-specialized
  path over SQL Server. This intentionally measures their combined end-to-end overhead and is not
  presented as isolated interface dispatch.
- `InquiryBuffering` compares an otherwise-identical custom class materializer (buffered by default)
  with the generated sequential-safe class.
- `AdoBufferingFloor` compares raw ADO.NET `SingleResult` with
  `SingleResult | SequentialAccess` over the same read loop.
- `ConsumptionMode` compares buffered-list return with full async streaming.
- `PartialStream` compares full enumeration with intentionally consuming one row and immediately
  disposing the async enumerator; its result represents work avoided by early termination, not an
  equal-cardinality speed ratio.

Both `MemoryDiagnoser` and `DisassemblyDiagnoser` are enabled. The suite requires Docker and a SQL
Server container. Omit `--job Dry` for publishable measurements and retained allocation/disassembly
artifacts:

```powershell
dotnet run -c Release --framework net10.0 -r win-x64 --project benchmarks\Inquiry.Benchmarks.SqlServer\Inquiry.Benchmarks.SqlServer.csproj -- --filter "*GeneratedAdHocSequentialAccessBenchmarks*"
```

The explicit Windows RID overrides the benchmark project's Linux CI default; omit it on Linux.

## Dataset size (`[Params(1000, 100000)]`)

The read-oriented classes — both CRUD classes and `PaginationBenchmarks`,
`ProjectionAggregateBenchmarks`, `PredicateBenchmarks`, `EagerLoadingBenchmarks`,
`EagerGridBenchmarks`, `EagerGridMixedRelationBenchmarks` — carry a
`[Params(1000, 100000)] public int Rows;` field, so BenchmarkDotNet runs every benchmark at
**two dataset tiers**: a small **1 000-row** set and a large **100 000-row** set. This shows
how each library's overhead scales: per-call fixed cost dominates at 1 000 rows, while
materialization / streaming cost dominates at 100 000. `BatchBenchmarks` is intentionally
**not** parameterized — it is a fixed-size (500-row) write benchmark, not a read over the
seeded data.

The two eager-grid classes cross `Rows` with a second **density** parameter — `RegionCount`
(4 dense / 100 sparse) and `PostCount` respectively — so each tier is measured with both few
parents holding many children and many parents holding few. In
`EagerGridMixedRelationBenchmarks`, `Rows` is the **tag** (child collection) count rather than a
per-entity row count.

## Setup

Each benchmark class creates a fresh SQLite database in `%TEMP%`, applies the shared
`NorthwindSchema.SqliteDdl`, and seeds `Rows` rows of each benchmarked entity (plus a handful
of categories so `Products.CategoryID` has valid FKs). Seeding happens **once per parameter
value in `[GlobalSetup]`** — so the 100 000-row insert is paid a single time per class/tier,
outside the measured region, not on every invocation. The file is deleted on `[GlobalCleanup]`.

`EagerGridMixedRelationBenchmarks` is the exception: it calls `CreateAsync(seedRows: 0)` and seeds only
its own three tables, because it never reads the Northwind entities and ~300k unused inserts is pure
setup cost. Its `[GlobalSetup]` also runs all three legs once through `AssertLegsAgree()`, which fails
the run unless they stitch the same number of relations — otherwise a regression that stopped populating
a navigation property would make one leg do less work and report as *faster*.

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
