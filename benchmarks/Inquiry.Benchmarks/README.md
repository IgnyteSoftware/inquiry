# Inquiry.Benchmarks

BenchmarkDotNet harness comparing basic CRUD performance of Inquiry, EF Core, Dapper, and
raw ADO.NET. Benchmark results are for internal testing: they are not committed, and nothing gates a
change or a release on them. Two suites:

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

Three invariants keep the comparison honest:

- **Matching columns.** Every read leg reads the same columns. The EF Core, linq2db and RepoDB
  customer and product entities map every column of the table, as the Inquiry entities do. They once
  mapped about half of them, which made those legs print 0.70x to 0.87x of the ADO.NET baseline on
  large reads. The write legs are narrower: the ADO.NET, Dapper, EF Core, linq2db and RepoDB legs set
  the five customer or six product columns that the handwritten SQL names, and the extra columns are
  read only in those entities. The Inquiry legs write the whole entity, because a generated insert or
  update always does.
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
  never get. The one exception is the `_EfCoreTuned` legs of `ShipperReadCompetitorBenchmarks`, which
  pool contexts on purpose (see below).

**Gate:** in the in-process SQLite suite, no non-baseline leg should print a `Ratio` below
~1.00 (modulo noise). A wrapper under 1.00 means the baseline has drifted out of parity — fix
the baseline, not the wrapper.

**Exempt: the grid-eager classes** (`EagerGridBenchmarks`, `EagerGridMixedRelationBenchmarks`). There the
ADO.NET baseline deliberately runs *separate* queries and stitches in memory, while Inquiry issues a
single multi-result-set command — so a `Ratio` or `Alloc Ratio` below 1.00 is the architectural
difference being measured, not baseline drift. Do not "fix" it.

Ratios from an earlier `EagerGridBenchmarks` run (`Grid_Inquiry` ÷ `Grid_AdoNet`, median): 0.97 at
(1000, 4), 0.99 at (1000, 100), 0.97 at (100000, 4), and 0.92 at (100000, 100). The post-#70 default-job
remeasurement for [#265](https://github.com/IgnyteSoftware/inquiry/issues/265) no longer reproduces the
former 1.15 dense-100k outlier. `EagerLoadingBenchmarks` is **not** exempt — it runs 1.11–1.13× and the
ordinary rule applies.

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
re-implementing the feature in client code. linq2db uses its own `InsertOrUpdateAsync`, which sends the
same single `INSERT … ON CONFLICT DO UPDATE` statement. Its `InsertOrReplaceAsync` rejects the IDENTITY
keys of `Product` and `Shipper`.

Every Update leg, in these classes and in each provider's `ShipperBenchmarks`, alternates between
two payloads that change every column it sets. With constant values, EF Core change tracking finds
nothing to save after the first call and sends no `UPDATE`, and MySQL and MariaDB skip the row write.
Each leg throws unless its call affects exactly one row: the returned row count must be 1, or the
returned `bool` must be `true` for Inquiry and DLG. DLG returns only whether its procedure ran, so
`ShipperUpdateLegTests` in `tests/Inquiry.Benchmarks.SqlServer.Tests` reads the row back after each
DLG call to prove the update.

### Cold start (`ColdStartBenchmarks`)

Each case measures the first point read after `GlobalSetup` in a fresh process. The job uses
`RunStrategy.ColdStart` with 10 launches of one call each, so no jitting, pilot or warmup call runs
before the measured one. The result excludes process launch, database seeding and what setup already
initialized (the SQLite provider, the DI container). Do not pass `--inProcess`, because every launch
then shares one process. The class has no `MemoryDiagnoser`, because
BenchmarkDotNet measures allocation on an extra call after the measured one.

```powershell
dotnet run -c Release -f net10.0 --project benchmarks\Inquiry.Benchmarks -- --filter "*ColdStartBenchmarks*"
```

### Tuned and source-generated competitors (`ShipperReadCompetitorBenchmarks`)

`SelectAll` and `SelectByKey` on `Shippers` (1 000 and 100 000 rows) against every competitor, each in its
plain and its tuned or source-generated form:

| Leg | Configuration |
| --- | --- |
| `_AdoNet` | Baseline, the same reader code as `ShipperCrudBenchmarks` |
| `_Dapper` | Reflection-based Dapper |
| `_DapperAot` | [Dapper.AOT](https://aot.dapperlib.dev/): the same Dapper call, intercepted by the Dapper.AOT source generator |
| `_EfCore` | Non-pooled context, LINQ query translated on each call |
| `_EfCoreTuned` | `EF.CompileAsyncQuery`, a `PooledDbContextFactory` and the compiled model in `Ef/CompiledModels` |
| `_LinqToDb` | linq2db LINQ query |
| `_LinqToDbCompiled` | linq2db `CompiledQuery.Compile` |
| `_RepoDb` | RepoDB |
| `_Inquiry` | Generated `ShipperStore` |

Every leg reads the same three columns, opens and disposes its own connection or context per call, and
buffers `SelectAll` into a list. `GlobalSetup` checks that every leg returns the same row count and the same
point-read row, and throws if one does not.

Dapper.AOT intercepts only methods marked `[DapperAot]`, so the plain `_Dapper` legs in this and every other
class stay reflection-based. The project enables the interceptors with
`<InterceptorsNamespaces>$(InterceptorsNamespaces);Dapper.AOT</InterceptorsNamespaces>`.

The compiled model is generated code. Regenerate it when `NorthwindDbContext` or the EF Core version
changes. The command also writes `NorthwindDbContextAssemblyAttributes.cs`, whose `[assembly: DbContextModel]`
attribute would make the plain EF Core legs use the compiled model too. The project file excludes it from
compilation, so only the tuned legs use the model, through `UseModel`:

```powershell
dotnet tool restore
dotnet tool run dotnet-ef dbcontext optimize --project benchmarks/Inquiry.Benchmarks --framework net10.0 --configuration Release --context Inquiry.Benchmarks.Ef.NorthwindDbContext --output-dir Ef/CompiledModels --namespace Inquiry.Benchmarks.Ef.CompiledModels
```

```powershell
dotnet run -c Release -f net10.0 --project benchmarks\Inquiry.Benchmarks -- --filter "*ShipperReadCompetitorBenchmarks*"
```

linq2db's async methods (`FirstOrDefaultAsync`, `ToListAsync`, `CountAsync` and the others) are in
`LinqToDB.Async.AsyncExtensions`. Every file here also imports `Microsoft.EntityFrameworkCore`, whose
methods of the same names bind first and throw for linq2db queries. The linq2db legs therefore call them
through the alias `L2Async`, for example `L2Async.FirstOrDefaultAsync(dc.GetTable<L2Shipper>(), predicate)`.

#### On PostgreSQL

`benchmarks/Inquiry.Benchmarks.PostgreSql` has a `ShipperReadCompetitorBenchmarks` class with the same legs and
the same `GlobalSetup` check. MySQL, MariaDB, SQL Server and Oracle have it too (see below). It differs from the
SQLite class in these ways:

- It has one row tier, `Rows = 1000`. The PostgreSQL container is shared by the whole process and seeded once
  by the first class that starts it.
- For the same reason, run it with its own filter when you use `--inProcess`. Other classes in the project seed
  one row or insert rows, and the setup check then throws, because the table does not hold `Rows` rows.
- The tuned EF Core legs use `PgShipperContext` with the compiled model in
  `benchmarks/Inquiry.Benchmarks.PostgreSql/Ef/CompiledModels`. The project file excludes
  `PgShipperContextAssemblyAttributes.cs` for the same reason as above.
- linq2db uses `UsePostgreSQL(connectionString, PostgreSQLVersion.v15)`, and RepoDB uses `RepoDb.PostgreSql`.

The project pins the `linux-x64` runtime. On Windows, build it and run the managed DLL. The provider `Program.cs`
does not return a failure exit code, so check the JSON report for cases without statistics:

```powershell
dotnet build benchmarks/Inquiry.Benchmarks.PostgreSql -c Release
dotnet benchmarks/Inquiry.Benchmarks.PostgreSql/bin/Release/net10.0/linux-x64/Inquiry.Benchmarks.PostgreSql.dll --filter "*ShipperReadCompetitorBenchmarks*" --inProcess --exporters json
```

Regenerate the compiled model from a Release build:

```powershell
dotnet build benchmarks/Inquiry.Benchmarks.PostgreSql -c Release -f net10.0
dotnet tool run dotnet-ef dbcontext optimize --project benchmarks/Inquiry.Benchmarks.PostgreSql --framework net10.0 --configuration Release --no-build --context Inquiry.Benchmarks.PostgreSql.Ef.PgShipperContext --output-dir Ef/CompiledModels --namespace Inquiry.Benchmarks.PostgreSql.Ef.CompiledModels
```

#### On MySQL and MariaDB

`benchmarks/Inquiry.Benchmarks.MySql` and `benchmarks/Inquiry.Benchmarks.MariaDb` each have a
`ShipperReadCompetitorBenchmarks` class with the same legs and the same `GlobalSetup` check as the PostgreSQL
class. The PostgreSQL notes apply: one row tier, `Rows = 1000`, and under `--inProcess` run the class with its
own filter. Here the other classes seed 1000 rows too, but `ShipperBenchmarks` inserts rows, so the setup check
throws after it. These parts differ:

| | MySQL (`mysql:8.0`) | MariaDB (`mariadb:11.4`) |
|---|---|---|
| Tuned EF Core | `MySqlShipperContext`, Pomelo with `MySqlServerVersion(8.0.0)` | `MariaDbShipperContext`, Pomelo with `MariaDbServerVersion(11.4.0)` |
| linq2db | `UseMySql(connectionString, MySqlVersion.MySql80, MySqlProvider.MySqlConnector)` | `UseMySql(connectionString, MySqlVersion.MariaDB10, MySqlProvider.MySqlConnector)` |
| RepoDB | `RepoDb.MySqlConnector` | `RepoDb.MySqlConnector` (RepoDB has no MariaDB package) |

Each compiled model is in the project's `Ef/CompiledModels`, and each project file excludes the generated
`*ContextAssemblyAttributes.cs`. Run and check them as for PostgreSQL:

```powershell
dotnet build benchmarks/Inquiry.Benchmarks.MySql -c Release
dotnet benchmarks/Inquiry.Benchmarks.MySql/bin/Release/net10.0/linux-x64/Inquiry.Benchmarks.MySql.dll --filter "*ShipperReadCompetitorBenchmarks*" --inProcess --exporters json
dotnet build benchmarks/Inquiry.Benchmarks.MariaDb -c Release
dotnet benchmarks/Inquiry.Benchmarks.MariaDb/bin/Release/net10.0/linux-x64/Inquiry.Benchmarks.MariaDb.dll --filter "*ShipperReadCompetitorBenchmarks*" --inProcess --exporters json
```

Regenerate the compiled models from a Release build:

```powershell
dotnet build benchmarks/Inquiry.Benchmarks.MySql -c Release -f net10.0
dotnet tool run dotnet-ef dbcontext optimize --project benchmarks/Inquiry.Benchmarks.MySql --framework net10.0 --configuration Release --no-build --context Inquiry.Benchmarks.MySql.Ef.MySqlShipperContext --output-dir Ef/CompiledModels --namespace Inquiry.Benchmarks.MySql.Ef.CompiledModels
dotnet build benchmarks/Inquiry.Benchmarks.MariaDb -c Release -f net10.0
dotnet tool run dotnet-ef dbcontext optimize --project benchmarks/Inquiry.Benchmarks.MariaDb --framework net10.0 --configuration Release --no-build --context Inquiry.Benchmarks.MariaDb.Ef.MariaDbShipperContext --output-dir Ef/CompiledModels --namespace Inquiry.Benchmarks.MariaDb.Ef.CompiledModels
```

#### On SQL Server

`benchmarks/Inquiry.Benchmarks.SqlServer` has a `ShipperReadCompetitorBenchmarks` class with the same legs and
the same `GlobalSetup` check as the PostgreSQL class. Every leg works on SQL Server, so none is left out. The
PostgreSQL notes apply: one row tier, `Rows = 1000`, and under `--inProcess` run the class with its own filter.
`BatchMutationStrategyBenchmarks` and `WideBatchInsertThresholdBenchmarks` seed one row, and `ShipperBenchmarks`
inserts rows, so the setup check throws after them. These parts differ:

- The tuned EF Core legs use `SqlServerShipperContext` with the compiled model in
  `benchmarks/Inquiry.Benchmarks.SqlServer/Ef/CompiledModels`. The project file excludes
  `SqlServerShipperContextAssemblyAttributes.cs` for the same reason as above.
- linq2db uses `UseSqlServer(connectionString, SqlServerVersion.v2022, SqlServerProvider.MicrosoftDataSqlClient)`,
  and RepoDB uses `RepoDb.SqlServer`.
- Dapper.AOT also checks the T-SQL of plain ADO.NET commands in a project that uses `Microsoft.Data.SqlClient`.
  `.editorconfig` turns off `DAP200`, `DAP225`, `DAP241` and `DAP242` for the four files that report them, because the fixture and evidence commands
  build DDL and catalog queries from constants in code.

Run and check it as for PostgreSQL:

```powershell
dotnet build benchmarks/Inquiry.Benchmarks.SqlServer -c Release
dotnet benchmarks/Inquiry.Benchmarks.SqlServer/bin/Release/net10.0/linux-x64/Inquiry.Benchmarks.SqlServer.dll --filter "*ShipperReadCompetitorBenchmarks*" --inProcess --exporters json
```

Regenerate the compiled model from a Release build:

```powershell
dotnet build benchmarks/Inquiry.Benchmarks.SqlServer -c Release -f net10.0
dotnet tool run dotnet-ef dbcontext optimize --project benchmarks/Inquiry.Benchmarks.SqlServer --framework net10.0 --configuration Release --no-build --context Inquiry.Benchmarks.SqlServer.Ef.SqlServerShipperContext --output-dir Ef/CompiledModels --namespace Inquiry.Benchmarks.SqlServer.Ef.CompiledModels
```

#### On Oracle

`benchmarks/Inquiry.Benchmarks.Oracle` has a `ShipperReadCompetitorBenchmarks` class with the same legs and the
same `GlobalSetup` check as the PostgreSQL class. No leg is left out, but the RepoDB legs differ (see below). The
PostgreSQL notes apply: one row tier, `Rows = 1000`, and under `--inProcess` run the class with its own filter.
Here the other classes seed 1000 rows too, but `ShipperBenchmarks` inserts rows, so the setup check throws after
it. These parts differ:

- The SQL and the competitor entities use the uppercase names that Oracle stores for the unquoted DDL
  (`SHIPPERS`, `SHIPPERID`, `COMPANYNAME`, `PHONE`). The point read binds `:id`.
- The tuned EF Core legs use `OracleShipperContext` with the compiled model in
  `benchmarks/Inquiry.Benchmarks.Oracle/Ef/CompiledModels`. The project file excludes
  `OracleShipperContextAssemblyAttributes.cs` for the same reason as above.
- linq2db uses `UseOracle(connectionString, OracleVersion.v12, OracleProvider.Managed)`.
- RepoDB uses `RepoDb.Oracle`. Its setup sets `OracleConfiguration.BindByName` for the whole process, and
  ODP.NET rejects that after the first connection opens (`ORA-50099`). `OracleBenchmarkDatabase` therefore
  calls `GlobalConfiguration.Setup().UseOracle()` before it starts the shared container, whichever class
  runs first. Every other leg binds by name already, so the setting changes no other leg.
- `SelectAll_RepoDb` calls `ExecuteQueryAsync<RdShipper>` with the ADO.NET statement. The `QueryAllAsync`
  statement of `RepoDb.Oracle` 1.16.0 ends with a semicolon, which Oracle rejects (`ORA-00933`), and `QueryAsync`
  does not accept an empty filter. RepoDB still maps the rows. `SelectByKey_RepoDb` uses `QueryAsync` as on
  the other providers.

Run and check it as for PostgreSQL:

```powershell
dotnet build benchmarks/Inquiry.Benchmarks.Oracle -c Release
dotnet benchmarks/Inquiry.Benchmarks.Oracle/bin/Release/net10.0/linux-x64/Inquiry.Benchmarks.Oracle.dll --filter "*ShipperReadCompetitorBenchmarks*" --inProcess --exporters json
```

Regenerate the compiled model from a Release build:

```powershell
dotnet build benchmarks/Inquiry.Benchmarks.Oracle -c Release -f net10.0
dotnet tool run dotnet-ef dbcontext optimize --project benchmarks/Inquiry.Benchmarks.Oracle --framework net10.0 --configuration Release --no-build --context Inquiry.Benchmarks.Oracle.Ef.OracleShipperContext --output-dir Ef/CompiledModels --namespace Inquiry.Benchmarks.Oracle.Ef.CompiledModels
```

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
point fixes that. **`Allocated` is the actionable memory signal** — it reproduces to the byte.

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
provider-parameter creation. `[MemoryDiagnoser]` reports managed allocation.

Both classes write JIT assembly reports, for auditing closure, delegate, and tuple costs, only when
`INQUIRY_BENCH_DISASM=1` is set. The disassembler reads the running benchmark process through the
runtime's DAC, and on Linux with .NET 8 that can crash the BenchmarkDotNet host with SIGSEGV and stop
the suite (#516). Leave the variable unset for measurement and release-evidence runs.

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
# Retained-command binder/JIT floor. Omit --job Dry for publishable measurements.
# For disassembly, also omit --job Dry and first run: $env:INQUIRY_BENCH_DISASM = '1'
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

### Handwritten-SQL name mapping ([#475](https://github.com/IgnyteSoftware/inquiry/issues/475))

`AdHocNameMappingBenchmarks` exists twice: in the in-process SQLite suite (`Products`, 10 columns)
and in the SQL Server suite (`BenchmarkSequentialAdHoc`, 14 columns with a 64 KB final
`VARBINARY(MAX)`, 32 rows, read with `SequentialAccess`). Every leg runs the same pipeline path and SQL.
Only the materializer differs:

- `Ordinal` is the fixed-ordinal read that ad-hoc materializers used before #475 (baseline).
- `CheckedOrdinal` is option 3 of the [#420 decision](../../docs/site/develop/api-contract-decisions.md#420-safe-defaults-for-ad-hoc-result-mapping):
  check every column name once per result set, then read by ordinal.
- `NameMapped` is the shipped option 2 generated materializer with columns in property order.
- `NameMappedReordered` reverses the SELECT list, so the reordered read path runs. On SQL Server the
  64 KB column then comes first.
- `NameMappedClassPath` (SQLite only) goes through the DI class materializer that `IInquiry.Query*<T>`
  resolves.

```powershell
dotnet run -c Release -f net10.0 --project benchmarks\Inquiry.Benchmarks -- --filter "*AdHocNameMappingBenchmarks*"
dotnet run -c Release --framework net10.0 -r win-x64 --project benchmarks\Inquiry.Benchmarks.SqlServer\Inquiry.Benchmarks.SqlServer.csproj -- --filter "*AdHocNameMappingBenchmarks*"
```

Default-job results, recorded 2026-10-22 on Windows 11, Intel Core i9-12900K, .NET 10.0.13. SQL Server
ran in a local Docker container. Mean, with the ratio to `Ordinal`:

| Suite | Category | Ordinal | CheckedOrdinal | NameMapped | NameMappedReordered | NameMappedClassPath |
|---|---|--:|--:|--:|--:|--:|
| SQLite | SingleRow (1 000 rows seeded) | 24.26 us | 24.79 us (1.02) | 24.76 us (1.02) | 24.93 us (1.03) | 24.96 us (1.03) |
| SQLite | SingleRow (100 000 rows seeded) | 23.85 us | 24.92 us (1.05) | 25.20 us (1.06) | 24.65 us (1.03) | 24.83 us (1.04) |
| SQLite | List, 1 000 rows | 1,093.8 us | 1,061.1 us (0.97) | 1,097.7 us (1.00) | 1,109.8 us (1.01) | 1,096.0 us (1.00) |
| SQLite | List, 100 000 rows | 108,961 us | 113,903 us (1.05) | 113,905 us (1.05) | 112,329 us (1.03) | 111,109 us (1.02) |
| SQL Server | SingleRow | 721.5 us | 711.7 us (0.99) | 705.5 us (0.98) | 711.1 us (0.99) | — |
| SQL Server | WideList, 32 × 64 KB | 7,522.8 us | 7,575.9 us (1.01) | 7,526.3 us (1.00) | 7,552.2 us (1.00) | — |
| SQL Server | FirstRowThenDispose | 7,432.8 us | 7,414.1 us (1.00) | 7,437.6 us (1.00) | 7,467.1 us (1.01) | — |

Name mapping (option 2) costs the same as checked ordinals (option 3) on the single-row path. The
difference is inside the run-to-run error of about ±0.2–0.4 us, so the option 3 fallback in the
decision does not apply. Both options call `DbDataReader.GetName` for every column once per result
set. On SQLite that costs about 0.5–1 us and 0.6 KB per query, because Microsoft.Data.Sqlite creates a
new string for each name. On SQL Server, the names are already cached, and every leg is within noise of
the ordinal baseline, including wide rows, a large final column read first, and early stream
disposal. Reordered columns and the class path add no measurable cost, and repeated reads of one
result shape allocate no map.

### Cancelable caller tokens ([#507](https://github.com/IgnyteSoftware/inquiry/issues/507))

`CancelableTokenBenchmarks` exists in the SQLite suite and in each provider project (SQL Server,
PostgreSQL, MySQL, MariaDB, Oracle). It runs a point read (`ShipperStore.SelectByKeyAsync`), a non-query
(`ShipperStore.UpdateAsync` on a fixed key) and a scalar (`IInquiry.ExecuteScalarAsync<string>`), each
against an ADO.NET baseline that passes the same token to every driver call. `TokenMode` picks the
token: `None` is the default token, `Reused` is one never-cancelled source for the whole run, and `Fresh`
is a new source per call, the per-request shape of ASP.NET Core.

Compare a cancelable mode with `None` to see what the token costs. The ADO.NET legs show the driver's own
cost, so `(Inquiry cancelable − Inquiry None) − (ADO.NET cancelable − ADO.NET None)` is what Inquiry adds.
With `Reused`, a registration reuses a callback node from the source's free list. With `Fresh`, each
registration that Inquiry adds costs one more node.

`CancellationHelperBenchmarks` (SQLite suite only) measures the cancellation race helper on its own,
against awaiting the same task directly. `Completed` is the task shape that Microsoft.Data.Sqlite returns.
`Pending` is the shape of a networked driver, which SQLite cannot show.

```powershell
dotnet run -c Release -f net10.0 --project benchmarks\Inquiry.Benchmarks -- --filter "*CancelableTokenBenchmarks*" "*CancellationHelperBenchmarks*"
dotnet run -c Release -f net10.0 -r win-x64 --project benchmarks\Inquiry.Benchmarks.PostgreSql -- --filter "*CancelableTokenBenchmarks*" --inProcess
```

Run each provider project the same way. `--inProcess` lets the cases share one container.

### Wide rows and large objects (`WideRowLobBenchmarks`)

`WideRowLobBenchmarks` exists in the SQLite suite and in each provider project (SQL Server, PostgreSQL,
MySQL, MariaDB, Oracle). It reads the 17 mapped columns of `Employees`: all 100 rows (`WideList`) and one
row by key (`WideByKey`). `NotesLength` sets the size of the `Notes` value in each row. At 256 characters
the row is wide but small. At 65 536 characters `Notes` is a large object and dominates the row. The
column is `TEXT` on SQLite and PostgreSQL, `NVARCHAR(MAX)` on SQL Server, `LONGTEXT` on MySQL and MariaDB,
and `CLOB` on Oracle.

| Leg | Configuration |
| --- | --- |
| `_AdoNet` | Baseline. Reads every column in ordinal order with `SequentialAccess`, as the generated store does |
| `_Dapper` | Reflection-based Dapper into the Inquiry `Employee` type |
| `_DapperAot` | The same Dapper call, intercepted by Dapper.AOT |
| `_Inquiry` | Generated `EmployeeStore` |

Every leg buffers `WideList` into a list. Each `GlobalSetup` deletes the `Employees` rows, inserts 100 rows
through `EmployeeStore.InsertAsync`, and throws unless every leg reads the same number of `Notes`
characters and the same row by key. No other class reads `Employees`, so on the provider projects the class
can share a process with other classes.

Left out:

- EF Core, linq2db and RepoDB have no legs. The benchmark projects map only customers, products and
  shippers for them (shippers only on most provider projects), so each leg needs a new 17-column entity
  in each of the six projects.
- The mapped `Employee` type has no binary column, so no leg reads `Photo`. On SQL Server,
  `GeneratedAdHocSequentialAccessBenchmarks` reads a 64 KiB `VARBINARY(MAX)` column.

```powershell
dotnet run -c Release -f net10.0 --project benchmarks\Inquiry.Benchmarks -- --filter "*WideRowLobBenchmarks*"
dotnet build benchmarks/Inquiry.Benchmarks.PostgreSql -c Release
dotnet benchmarks/Inquiry.Benchmarks.PostgreSql/bin/Release/net10.0/linux-x64/Inquiry.Benchmarks.PostgreSql.dll --filter "*WideRowLobBenchmarks*" --inProcess
```

Run each provider project the same way.

### Many-to-many reads (`ManyToManyBenchmarks`)

`ManyToManyBenchmarks` exists in the SQLite suite and in each provider project. It eager-loads
`BenchmarkM2MOrder.Products` through the `BenchmarkM2MOrderProduct` junction table
(`[InquiryManyToMany]`): every order with its products (`AllParents`, `[InquirySelectAllEager]`) and one
order with its products (`OneParent`, `[InquirySelectOneByKeyEager]`).

| Leg | Configuration |
| --- | --- |
| `_AdoNet` | Baseline. One command with the same statements, in the same result-set order, as the generated store |
| `_Dapper` | `QueryMultipleAsync` over the same command |
| `_Inquiry` | Generated `BenchmarkM2MOrderStore` |

For `AllParents` the command returns the linked products, the links and the orders, and each leg attaches
the products to their orders in memory. For `OneParent` it returns the order and its products from a join.
The ordinary rule applies: the baseline does the same work as the store, so a ratio below 1.00 means the
baseline is wrong. On Oracle the statements are in one PL/SQL block that returns implicit results, because
Oracle has no multi-statement batch. `GlobalSetup` throws unless every leg attaches the same number of
products.

The data differs by project:

- SQLite, PostgreSQL, MySQL, MariaDB and Oracle: the first setup seeds 1 000 products and 100 orders with
  ten products each. Half of the products have no link, so the product query must filter them out. No
  other class uses the tables.
- SQL Server: the entities, the store and the tables were already there for the `ManyToManyChildFiltering`
  legs of `EagerLoadingBenchmarks`. The class reads the data that the fixture seeds for those legs: two
  orders, eight linked products and 1 000 products without a link.

EF Core, linq2db and RepoDB have no legs. The benchmark projects have no many-to-many mapping for them.

```powershell
dotnet run -c Release -f net10.0 --project benchmarks\Inquiry.Benchmarks -- --filter "*ManyToManyBenchmarks*"
dotnet build benchmarks/Inquiry.Benchmarks.PostgreSql -c Release
dotnet benchmarks/Inquiry.Benchmarks.PostgreSql/bin/Release/net10.0/linux-x64/Inquiry.Benchmarks.PostgreSql.dll --filter "*ManyToManyBenchmarks*" --inProcess
```

Run each provider project the same way.

### Bulk copy (`BulkCopyBenchmarks`)

`BulkCopyBenchmarks` exists in the SQL Server, MySQL and MariaDB projects. It copies 5 000 shippers through
the provider's native bulk API: `SqlBulkCopy` on SQL Server and `MySqlBulkCopy` on MySQL and MariaDB.

| Leg | Configuration |
| --- | --- |
| `_AdoNet` | Baseline. The native bulk API, fed by a forward-only data reader, with the columns mapped by name |
| `_AdoNetBuffered` | SQL Server only. The baseline with `EnableStreaming` off, the `SqlBulkCopy` default |
| `_LinqToDb` | `BulkCopyAsync` with `BulkCopyType.ProviderSpecific` |
| `_Inquiry` | `ShipperStore.BulkInsertAsync` (`[InquiryBulkInsert]`) |

Each iteration deletes the shippers first. `GlobalSetup` runs every leg once and throws unless the leg
reports 5 000 rows and the table then holds 5 000 rows.

- **SQL Server.** The Inquiry provider turns on `SqlBulkCopy.EnableStreaming`, so the baseline turns it on
  too and both legs do the same driver work. `Shippers.Phone` is `NVARCHAR(MAX)`, and with streaming on,
  `SqlBulkCopy` allocates about twelve times as much for these rows (33.7 MB against 2.75 MB in a Dry
  run). The extra `_AdoNetBuffered` leg keeps the `SqlBulkCopy` default, which is off, to show that
  difference. It exists on SQL Server only.
- **MySQL and MariaDB.** `MySqlBulkCopy` sends `LOAD DATA LOCAL INFILE`. The fixtures start the server
  with `--local-infile=1`, and the ADO.NET and linq2db legs use a connection string with
  `AllowLoadLocalInfile=true`. Inquiry sets the same option on the connection that it opens for a bulk
  insert.

Left out:

- Dapper and EF Core have no bulk-copy API. RepoDB has one for SQL Server only, in
  `RepoDb.SqlServer.BulkOperations`, which no project references.
- PostgreSQL already has `BulkInsertBenchmarks`, which compares chunked `[InquiryInsert]` batches with one
  binary `COPY` (`[InquiryBulkInsert]`).
- SQLite and Oracle have no bulk path. `[InquiryBulkInsert]` compiles to the multi-row batch `INSERT`
  there, which `BatchBenchmarks` and `BatchMutationStrategyBenchmarks` measure.

The class deletes the shippers that other classes read from the shared container. Under `--inProcess`
run it with its own filter:

```powershell
dotnet build benchmarks/Inquiry.Benchmarks.SqlServer -c Release
dotnet benchmarks/Inquiry.Benchmarks.SqlServer/bin/Release/net10.0/linux-x64/Inquiry.Benchmarks.SqlServer.dll --filter "*BulkCopyBenchmarks*" --inProcess
```

### Connection-pool pressure (`PoolPressureBenchmarks`)

`PoolPressureBenchmarks` exists in each provider project (SQL Server, PostgreSQL, MySQL, MariaDB, Oracle).
In one operation, 16 concurrent callers each run one shipper point read. `MaxPoolSize` caps the connection
pool in the connection string that every leg uses:

- `MaxPoolSize = 2`: eight callers for each connection, so callers wait for the pool.
- `MaxPoolSize = 16`: one connection for each caller, so no caller waits. Compare a leg with itself at this
  tier to see what the pressure costs it.

The legs are the nine point reads of `ShipperReadCompetitorBenchmarks`: `_AdoNet` (baseline), `_Dapper`,
`_DapperAot`, `_EfCore`, `_EfCoreTuned`, `_LinqToDb`, `_LinqToDbCompiled`, `_RepoDb` and `_Inquiry`. Each
caller opens and closes its own connection or context. The plain EF Core leg constructs a context for each
call, and the tuned leg takes one from a context pool, as in that class. The Inquiry leg has its own
service provider, because the fixture's provider uses the connection string without the cap.
`GlobalSetup` checks that every leg reads the same row and that every leg finds a row for all 16 callers.

A library that builds its own data source from the connection string has its own pool with the same cap.
With Npgsql, EF Core and Inquiry do that. So the legs do not all share one pool, and the idle connections
of earlier legs stay open. The tiers are small for that reason: the PostgreSQL container accepts 100
connections and the MySQL and MariaDB containers 151. With 64 callers the class failed with
`53300: sorry, too many clients already`.

SQLite is left out. Microsoft.Data.Sqlite has no maximum pool size, so a caller never waits for a
connection. `ConcurrencyBenchmarks` measures concurrent point reads there.

```powershell
dotnet build benchmarks/Inquiry.Benchmarks.PostgreSql -c Release
dotnet benchmarks/Inquiry.Benchmarks.PostgreSql/bin/Release/net10.0/linux-x64/Inquiry.Benchmarks.PostgreSql.dll --filter "*PoolPressureBenchmarks*" --inProcess
```

Run each provider project the same way.

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
dotnet build benchmarks/Inquiry.Benchmarks.SqlServer/Inquiry.Benchmarks.SqlServer.csproj -c Release -f net10.0
dotnet benchmarks/Inquiry.Benchmarks.SqlServer/bin/Release/net10.0/linux-x64/Inquiry.Benchmarks.SqlServer.dll --collection-smoke
```

Use that managed-DLL form with `--collection-benchmark`, `--collection-verify`, or
`--collection-evidence <path>` for the corresponding Windows command below.

```powershell
dotnet run -c Release -f net10.0 --project benchmarks/Inquiry.Benchmarks.SqlServer -- --collection-benchmark
dotnet run -c Release -f net11.0 --project benchmarks/Inquiry.Benchmarks.SqlServer -- --collection-benchmark
```

The smoke commands only validate wiring and are not suitable for published comparisons:

```powershell
dotnet run -c Release -f net10.0 --project benchmarks/Inquiry.Benchmarks.SqlServer -- --collection-smoke
dotnet run -c Release -f net11.0 --project benchmarks/Inquiry.Benchmarks.SqlServer -- --collection-smoke
```

Correctness and server evidence run outside the timed BenchmarkDotNet path. Evidence contains logical
reads and hashed SQL/parameter/query/plan signatures; it intentionally contains no SQL text, raw rows,
hosts, secrets, or local paths.

The old `PlanCacheBenchmarks` suite was removed in full because it timed cache clearing and therefore
could not produce authoritative latency results. Its declared-size versus inferred-size experiment was
not silently carried forward; a separately controlled parameter-signature experiment remains explicit
follow-up work under #87.

```powershell
dotnet run -c Release -f net10.0 --project benchmarks/Inquiry.Benchmarks.SqlServer -- --collection-verify
dotnet run -c Release -f net10.0 --project benchmarks/Inquiry.Benchmarks.SqlServer -- --collection-evidence artifacts/benchmarks/sqlserver-collection-evidence.json
```

Local output and collected server evidence are for internal testing. They are not committed and do not
gate a release.

## Reading the results

Each benchmark class uses `[CategoriesColumn]` and groups by category, so the BDN summary
table prints one section per operation with all four implementations side-by-side. ADO.NET
is marked `[Baseline = true]` — the `Ratio` column shows each library's overhead relative
to hand-written ADO.NET.
