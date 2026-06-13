# Roadmap

> This page lists **open** work only — known issues, security follow-ups, performance ideas, and planned
> enhancements. Resolved items are summarized at the [bottom](#recently-resolved). Nothing here blocks
> `main`: the library builds and every test suite passes.
>
> **Last reconciled against the code:** 2026-06-12.

## Known issues & correctness

- *No open correctness bugs are currently known.* (The relation-const generator crash previously listed
  here is fixed — see [Recently resolved](#recently-resolved). A residual *diagnostics* gap — relation
  typos are only reported when the relation is eager-loaded — is tracked under
  [Planned features](#planned-features--enhancements).)

## Security

- *No open security follow-ups are currently known.* The formal Codex Security repository scan has been run;
  validated findings were fixed in `318ee5f` and summarized in [Security](../articles/security.md).

## Performance & optimization

> The items below came out of the 2026-06-12 competitive feature-gap research (vs EF Core, XPO,
> Dapper + ecosystem, and the JS/TS ORMs) and are ordered by expected impact. (`DbBatch` pipeline
> support shipped — see [Recently resolved](#recently-resolved).)

- **Table-valued parameters (SQL Server).** PostgreSQL array `IN` parameters shipped (see
  [Recently resolved](#recently-resolved)); SQL Server TVPs remain the sibling mechanism for passing
  sets to commands and stored procedures on that engine.
- **Single-round-trip eager loading.** Separate-query eager loading currently pays one round trip per
  relation; combining the parent + relation SELECTs into one multi-result-set command (Dapper
  `QueryMultiple`-style) keeps the design but cuts the latency to one round trip.

## Planned features & enhancements

> Items marked *(gap research 2026-06-12)* came out of the competitive feature-gap analysis vs
> EF Core, XPO, Dapper + ecosystem, and the JS/TS ORMs (Prisma, Drizzle, TypeORM, Sequelize, Kysely).
> Items marked *(adoption review 2026-06-12)* came out of the follow-up "what do companies actually
> hit" review; the first four are the highest-leverage adoption items on this page. Items marked
> *(integration research 2026-06-12)* came out of the cross-framework integration/DX research
> (Aspire, MassTransit/Wolverine, Spring/Micronaut, Rails/Laravel/Ecto, sqlc/sqlx/Atlas).

- **Aspire integration package** *(integration research 2026-06-12)*. An `Inquiry.Aspire` client
  integration in the standard shape every mainstream data library now ships: resolve the
  Aspire-provisioned connection string by resource name, register the provider factory, and
  auto-wire the existing telemetry (`AddInquiryTelemetry()`) and health check so Inquiry lights up
  the Aspire dashboard. Foundation work: build provider connection factories on
  **`System.Data.Common.DbDataSource`** (the .NET 7+ pooled primitive Aspire registers) instead of
  raw connection strings.
- **Build-time SQL validation against a dev database** *(integration research 2026-06-12)*. The
  Rust sqlx `query!` / Go sqlc model: because Inquiry's SQL is compile-time constant, an opt-in
  build step or test helper can `PREPARE`/`EXPLAIN` every generated SQL const against a
  dev/Testcontainers database, catching schema drift at build time. No .NET ORM offers this, and
  Inquiry is uniquely positioned — the internal schema-fidelity tests already prove the approach;
  this productizes it for consumers.
- **Dev-time query diagnostics** *(integration research 2026-06-12)*. An N+1 detector for the
  default interceptor library (Rails bullet/prosopite model — fingerprint repeated
  identical-SQL/different-parameter executions per scope and warn with call sites; no .NET ORM has
  this) plus an `ExplainAsync` helper surfacing the database query plan for any generated method
  (Django `QuerySet.explain()` analog).
- **Derived query methods** *(integration research 2026-06-12)*. Infer filter columns from the
  method name (`SelectByCompanyNameAsync`) so store attributes need no arguments in the common case
  — the Spring Data convention, done at compile time like Micronaut Data.
- **`[InquiryModifiedBy]` (who-changed-it auditing)** *(integration research 2026-06-12)*.
  Timestamp auditing shipped (see [Recently resolved](#recently-resolved)); the user/principal
  counterpart needs an ambient current-user accessor seam and pairs naturally with the audit-trail
  interceptor below.
- **`dotnet new` project templates** *(integration research 2026-06-12)*. An Aspire-ready starter
  template with a provider, telemetry, health checks, and tests wired from the first build.
- **DDL safety lint** *(integration research 2026-06-12)*. squawk-inspired analyzer warnings for
  risky patterns in generated DDL; small and fits the existing analyzer/diagnostic surface.
- **Testing follow-ups: transaction sandbox + data factories** *(adoption review 2026-06-12)*.
  The store interfaces and the `Inquiry.Testing` package (SQLite fixture, recording interceptor,
  Respawn reset) shipped — see [Recently resolved](#recently-resolved) and
  [Testing](../articles/features/testing.md). Remaining scope: an **Ecto-style SQL sandbox**
  (each test inside a rolled-back transaction with connection ownership, enabling parallel
  database tests) and **factory_bot/Laravel-style test-data factories** (states/sequences,
  Bogus-compatible).
- **JSON-path querying & column-encryption docs** *(adoption review 2026-06-12)*. Predicate support
  for filtering into JSON columns (EF parity), and documentation for SQL Server Always Encrypted /
  pgcrypto patterns over the existing value-converter seam (mostly docs, little code).
- **Release engineering & governance** *(adoption review 2026-06-12)*. Real `RepositoryUrl`
  (currently a placeholder), SourceLink + symbol packages, package readme/icon, a pack/publish
  workflow, and a published versioning / breaking-change / support-window policy — the remaining
  pre-1.0 go-live bucket.
- **Default interceptor library — remaining scope** *(gap research 2026-06-12)*. The
  `Inquiry.Interceptors` package shipped with slow-query warning logging and sqlcommenter
  trace-context tagging (see [Recently resolved](#recently-resolved)); the command-text assertion
  interceptor already lives in `Inquiry.Testing`. Remaining: audit trail (who/when/what changed —
  XPO's module as an interceptor; pairs with `[InquiryModifiedBy]`), DataAnnotations entity
  validation before insert/update (needs an entity-level seam — the command interceptor sees SQL,
  not entities), and the N+1 detector (see *Dev-time query diagnostics* above).
- **Read-replica routing** *(gap research 2026-06-12)*. Route SELECTs to a read-replica pool and pin
  mutations + transactions to the primary (Drizzle `withReplicas` / Sequelize / TypeORM semantics).
  No mainstream .NET ORM ships this; Inquiry already has the connection-factory and failover chassis
  to build on.
- **Stored-procedure INOUT parameters & multi-result-set** *(gap research 2026-06-12)*. Scalar
  OUTPUT parameters and the integer RETURN value now surface as a `Task<TScalar>` result (see
  [Recently resolved](#recently-resolved)); the remaining gaps are INOUT parameters (a value passed
  in *and* read back), multiple result sets, table-valued parameters, and Oracle `OUT REF CURSOR`.
- **Database-first scaffolding CLI** *(gap research 2026-06-12)*. A `dotnet inquiry scaffold` tool
  that introspects an existing database and emits attributed entities + store skeletons — the
  `dotnet ef dbcontext scaffold` / `prisma db pull` / `drizzle-kit pull` workflow. Largest effort,
  largest onboarding lever for existing databases.
- **Server-computed columns** *(gap research 2026-06-12)*. Computed-column DDL + materializer support
  for properties calculated by the database (EF `HasComputedColumnSql`, XPO persistent aliases).
- **Many-to-many relations** *(gap research 2026-06-12)*. Auto-managed junction tables for M:N
  associations; the relation model is currently 1:N / N:1 only.
- **CTEs and set operations** *(gap research 2026-06-12)*. `WITH` / `UNION` / `INTERSECT` / `EXCEPT`
  composition in the predicate/select model (Kysely-style); ad-hoc SQL covers this today.
- **Tenant/global query filters + Postgres RLS helpers** *(gap research 2026-06-12)*. Generalize the
  soft-delete global-filter machinery to user-defined columns (EF `HasQueryFilter` / EF 10 named
  filters), plus row-level-security session helpers for PostgreSQL (Drizzle RLS-style).
- **Provider-specific column types** *(gap research 2026-06-12)*. SQL Server **vector** columns first
  (EF 10 `SqlVector` parity — AI embeddings / semantic search); spatial and `hierarchyid` by demand.
- **Additional database engines** *(gap research 2026-06-12)*. XPO supports 15+ engines vs Inquiry's 5;
  add engines (Firebird, DB2, MariaDB-specific, …) demand-driven — the provider + analyzer split makes
  each one mechanical.
- **Verified cloud-platform compatibility matrix** *(post-1.0 — deferred to a later release)*. Most
  popular hosted databases are wire-compatible with engines Inquiry already ships, so this is
  compatibility modes + verified docs, not new dialects — extending the existing `Compatibility`
  enum pattern (`CockroachDb`, `AuroraPostgreSql`, `AzureSql`):
  - **Supabase** (Postgres): document/handle its Supavisor pooler — transaction-mode pooling breaks
    server-side prepared statements, so guide to `PreparedStatementMode.None` or session pooling;
    add a transient-error detector entry.
  - **Neon** (serverless Postgres): scale-to-zero cold starts make open-time retry essential
    (already built); add its documented transient codes.
  - **PlanetScale** (Vitess/MySQL): a compat mode that suppresses foreign-key DDL (Vitess
    historically rejects FKs) and documents eager-loading implications.
  - Lower priority, same pattern: YugabyteDB / AlloyDB / Timescale (Postgres wire), TiDB /
    SingleStore (MySQL wire). Turso/libSQL waits on a mature .NET client; DuckDB / ClickHouse are
    OLAP and out of scope unless demanded.
  - Where feasible, a scheduled CI leg per platform (Supabase local Docker stack, Vitess image) so
    "works with Supabase" stays test-proven rather than asserted.
- **Full-Northwind test & benchmark coverage.** The suites exercise a representative subset across the
  five engines; replicate the full Northwind entity/relationship surface (all tables, all CRUD + read
  shapes) across ADO.NET / Inquiry / Dapper / EF Core in both tests and benchmarks, so every feature is
  compared apples-to-apples on every entity.
- **Multi-database in one container.** Inquiry binds a single global `IInquiryConnectionFactory` per
  service collection (now enforced — registering two providers throws a clear exception). True
  multi-provider support would require keyed/named factories or per-provider store scopes — .NET 8
  keyed DI services are the natural mechanism *(integration research 2026-06-12)*.
- **CI: repo-wide warning gate.** Production projects are warnings-as-errors and the known warning
  sources are scoped-suppressed; a repo-wide build-warning gate (extending coverage to the test projects)
  would catch new warnings. *(Skip-gating and the scheduled full-TFM matrix are done — see
  [Recently resolved](#recently-resolved).)*
- **Optional Roslyn bump.** `Microsoft.CodeAnalysis.CSharp` is intentionally held at 4.8.0 to keep the
  analyzer's minimum-SDK floor low; revisit only if a newer Roslyn API is needed.
- **Telemetry enrichment.** The opt-in telemetry layer (see
  [Observability](../articles/features/observability.md)) emits OTel-conventional spans, a
  `db.client.operation.duration` histogram, and `ILogger` messages. Candidate follow-ups:
  a `db.collection.name` (table) span tag, sqlcommenter-style trace-context SQL comments, and
  connection-open / pool-wait instruments.
- **Broaden relation-shape diagnostics.** `INQ040` (unknown relation foreign key) and `INQ041`
  (composite-key child) fire only when an eager-loading method traverses the relation. A relation that is
  mistyped but never eager-loaded is silently skipped (it no longer crashes the generator), and a foreign
  key pointing at the wrong side has no dedicated diagnostic. Report these at declaration time regardless
  of eager usage. *Low severity — no crash, and no wrong results unless the relation is eager-loaded.*

### Explicitly not planned

- **Migrations Phase B** (schema diff / `ALTER` / versioning) — delegate to DbUp or FluentMigrator;
  Inquiry emits initial `CREATE TABLE` DDL only (`InquiryGeneratedSchema.Ddl`).
- **NoSQL / document engines** (Cosmos DB, MongoDB) — they don't fit a SQL-generating, schema-bound,
  JOIN/eager-loading model.
- **JOIN-based or lazy eager loading** — Inquiry's separate-query eager loading is the recommended
  high-performance pattern by design.
- **Inheritance mapping (TPH/TPT), dynamic/untyped rows, shadow properties** — all pull toward
  runtime-shaped mapping, against the compile-time, source-generated ethos (gap research 2026-06-12).
- **Data-browser GUIs** (Prisma/Drizzle Studio analogs) — a library concern, not an ORM concern; use
  existing database tools.
- **CDC/realtime and managed infrastructure services** (Prisma Pulse/Accelerate analogs) — products,
  not library features.
- **OData and LINQPad drivers** — both want an `IQueryable` provider, which Inquiry deliberately
  does not have (integration research 2026-06-12).
- **Orleans grain storage, Dapr state stores, Hangfire/Quartz job storage** — different abstraction
  layers; those frameworks manage their own persistence (integration research 2026-06-12).
- **Admin UIs, REPL consoles, and query dashboards** (Django admin / Laravel Telescope analogs) —
  the telemetry layer feeds existing dashboards; building one is a product, not a library feature
  (integration research 2026-06-12).
- **Schema-branching and migration-platform tooling** (Atlas, Neon/PlanetScale branch-per-PR) —
  external workflow tools; at most a docs pointer (integration research 2026-06-12).

## Recently resolved

Since the 2026-06-03 internal review, the following were fixed (each with regression tests) and are **not**
open:

- **View-mapped / keyless read-only entities (2026-06-13):** `[InquiryView("v_name")]` maps a
  read-only, keyless-permitted entity — a store over it may declare only SELECT/aggregate/count
  operations (mutations are INQ052), no `[InquiryKey]` is required, and the schema generator skips
  it (the view lives in the database, no DDL or FK constraints emitted). Discovered through the
  same `EntityData`/materializer/store-linking pipeline as tables (merged in via a second syntax
  provider), so projections, predicates, and field selects all work against the view. Live-proven
  on SQLite (`CREATE VIEW` aggregate round-trip) plus generator snapshots. See
  [View entities](../articles/features/view-entities.md).
- **Stored-procedure scalar output/return (2026-06-13):** `[InquiryStoredProcedure(OutputParameter =
  "@Name")]` / `[InquiryStoredProcedure(ReturnsValue = true)]` declare the method as
  `Task<TScalar>` and surface a single OUTPUT parameter (bound `ParameterDirection.Output` with its
  DbType, `Size = -1` for strings) or the integer RETURN value as the task result, read back through
  a new `IInquiry.ExecuteProcedureScalarAsync<T>` pipeline seam (both pipelines). The two knobs are
  mutually exclusive and a RETURN value must be `Task<int>` (new INQ051). Provider-uniform via
  `CommandType.StoredProcedure`; live-proven on SQL Server (`OUTPUT` + `RETURN` procs), plus generator
  snapshots. INOUT/multi-result-set/TVP/Oracle ref-cursor remain open. See
  [Stored procedures](../articles/features/stored-procedures.md).
- **Provider-native bulk copy (2026-06-13):** `[InquiryBulkInsert]` streams rows through
  `SqlBulkCopy` / Npgsql binary `COPY` / `MySqlBulkCopy` (new `IInquiryBulkCopier` registered by
  those provider packages; `IInquiry.BulkInsertAsync` + a generated
  `InquiryBulkInsertDefinition<T>` with converter/enum-aware ordinal accessors), returning
  `Task<long>` rows written with no parameter cap; SQLite/Oracle compile the method down to the
  multi-row batch insert. Sequential-GUID keys and auditing timestamps stamp per row as the
  stream is enumerated; bulk insert uses a dedicated connection (no ambient transaction,
  documented). Live tests on all five dialects via the shared `BulkItem` catalog fixture, and
  `BulkInsertBenchmarks` (PostgreSQL) compares chunked `VALUES` batches vs one binary `COPY`.
  See [Bulk insert](../articles/features/bulk-insert.md).
- **Inquiry.Interceptors package (2026-06-13):** opt-in companion package with
  `AddInquirySlowQueryLogging(threshold)` (warns with duration + command text — never parameter
  values — measuring the provider round trip via a `ConditionalWeakTable`-correlated
  executing/executed pair) and `AddInquirySqlCommenter(application)` (sqlcommenter-style
  `application`/W3C `traceparent` SQL comments from `Activity.Current` for DBA-side trace
  correlation; skips already-commented text, documented prepared-reuse trade-off). Core stays
  dependency-free. See [Interceptors](../articles/features/interceptors.md).
- **Data-seeding convention (2026-06-13):** `IInquiryDataSeeder` + `AddInquirySeeder<T>()`
  (scoped, registration-ordered, duplicate-safe via `TryAddEnumerable`) and
  `IServiceProvider.SeedInquiryAsync()` (one scope, sequential, explicit invocation only) —
  the EF `UseSeeding`/`prisma db seed` analog. The Blazor sample's 13-table `DataSeeder` now
  runs through the hook. See [Data seeding](../articles/features/data-seeding.md).
- **Auditing timestamp columns (2026-06-12):** `[InquiryCreatedAt]`/`[InquiryModifiedAt]` on a
  `DateTime`/`DateTimeOffset` property — insert/upsert stamp `CreatedAt` when unset (supplied
  values kept) and every generated insert/update/upsert (incl. batch forms) stamps `ModifiedAt`;
  `CreatedAt` is excluded from every UPDATE SET and bind across all five dialects (incl. upsert
  conflict branches and MySQL's `VALUES()` form), so a constructed entity can't clobber the stored
  creation time. Invalid placement/type is INQ049, duplicates INQ050. Set-based mutations and
  soft-delete/restore intentionally don't stamp. See
  [Auditing timestamps](../articles/features/auditing.md).
- **Docs round (2026-06-12):** a documented **`TransactionScope`/System.Transactions position**
  (per-operation auto-enlistment behavior, the MSDTC escalation trap, recommended patterns; the
  explicit enlistment API stays unplanned unless demanded) in
  [Transactions](../articles/features/transactions.md); a **migrations recipe**
  ([Migrations](../articles/features/migrations.md)) wiring `InquiryGeneratedSchema.Ddl` into
  DbUp/FluentMigrator with a schema-drift CI practice; and a **GraphQL DataLoader recipe**
  ([GraphQL DataLoader](../articles/features/graphql-dataloader.md)) — Hot Chocolate
  `BatchDataLoader`/`GroupedDataLoader` over `Compare.In` batch selects.
- **Raw-SQL injection analyzer (2026-06-12):** new `InquiryRawSqlAnalyzer` (ships in every
  provider's analyzer assembly) warns with **INQ048** when a non-constant string reaches
  `InquiryCommand`'s command text — literals, consts, `nameof`, and constant concatenation stay
  silent; generated code is excluded. Documented in [Security](../articles/security.md).
- **PostgreSQL array `IN` parameters (2026-06-12):** `Compare.In` predicates, `[InquiryDeleteAll]`,
  and IN criteria on set-based mutations now render `col = ANY(@ids)` on PostgreSQL and bind the
  collection as one native array parameter (new `InquiryArrayParameter`; enum elements coerce to
  their underlying type, empty lists bind an empty array). The SQL stays constant across list
  lengths — prepared statements stay reusable and the per-element parameter cap no longer applies
  to IN lists there. Other dialects keep sentinel expansion. A new `InListBenchmarks` harness in
  `Inquiry.Benchmarks.PostgreSql` compares the array path against sentinel expansion across
  cycling list cardinalities (1/5/20/100).
- **Sequential `Guid` v7 keys (2026-06-12):** `[InquiryKey(SequentialGuid = true)]` makes
  insert/upsert/batch-insert assign a time-ordered UUID v7 via the new public
  `InquiryGuid.NewVersion7()` (delegates to `Guid.CreateVersion7()` on .NET 9+, RFC 9562 polyfill
  on .NET 8) whenever the key is unset; supplied keys are never overwritten, the caller observes
  the generated key, and misuse (non-Guid / `IsGenerated` / `UseDatabaseDefault` keys) is a
  build-time error (new INQ047). See the key-generation section in
  [CRUD](../articles/features/crud.md).
- **Transactional-outbox enablement (2026-06-12):** `IInquiryTransaction` now exposes its live
  `Connection` and `DbTransaction` (fail-fast after close; a savepoint handle surfaces the outer
  pair), so MassTransit/Wolverine-style outbox writes can enlist in the active Inquiry transaction
  and commit atomically with entity work. Documented under
  [Transactions](../articles/features/transactions.md) with ownership rules (borrowed, never
  committed/disposed by the caller); default interface implementations keep existing test doubles
  source-compatible.
- **Ad-hoc DTO materialization (2026-06-12):** `[InquiryAdHoc]` on a plain class or record generates
  an ordinal-reading materializer (publicly settable properties in declaration order, no per-property
  attributes; `[InquiryEnumAsString]` honored) and registers it via `AddInquiryGeneratedStores()`, so
  the ad-hoc `IInquiry.Query*` methods map hand-written reporting SQL (joins, GROUP BY) into POCOs
  that are neither entities nor projections — closing the "I'd just use Dapper for this one query"
  escape. Property-less or non-constructible DTOs (e.g. positional records) are rejected at build
  time (new INQ045/INQ046). See [Ad-hoc DTOs](../articles/features/ad-hoc-dtos.md).
- **Set-based predicate mutations (2026-06-12):** `[InquiryUpdateWhere(setFields…)]` and
  `[InquiryDeleteWhere]` (with `HardDelete`) — UPDATE/DELETE by `[InquiryWhere]` predicate without
  loading entities, reusing the compile-time predicate model (IN expansion included). Soft-delete
  entities get the soft UPDATE form and compose the active-row filter; concurrency-token entities are
  rejected (INQ022); at least one predicate is required (new INQ023) and SET fields are validated
  (new INQ044). See [Set-based mutations](../articles/features/set-based-mutations.md).
- **Configuration binding (2026-06-12):** every provider gained
  `AddInquiry{Provider}(IConfiguration, connectionStringName = "Inquiry")` overloads (+ options
  variants) resolving `ConnectionStrings:{name}` with an actionable error on a missing key.
- **`DbBatch` pipeline support (2026-06-12):** `IInquiry.ExecuteBatchAsync` executes one command text
  per item with per-item parameters — a single ADO.NET `DbBatch` round trip on Npgsql / SqlClient /
  MySqlConnector (capability-probed), sequential same-connection execution elsewhere.
  `[InquiryUpdateAll]` now routes through it reusing the single-row `_sqlUpdate` const: the
  multi-statement `{r}`-template machinery and per-row parameter mangling are gone, the UpdateAll
  parameter cap no longer applies, and **Oracle UpdateAll works** (the `INQ039` stub is removed; live
  Oracle batch-update test added). Interceptors fire per item on the sequential path only.

- **Adoption round 1 (2026-06-12):** opt-in **generated store interfaces** —
  `[InquiryGenerateInterface]` emits `I{Store}` (signatures with defaults preserved), the generated
  partial implements it, and DI forwards the interface to the same scoped store instance, making
  stores mockable; the **`Inquiry.Testing` package** (SQLite fixture, recording command interceptor
  with assertion helpers, Respawn reset wrapper — see [Testing](../articles/features/testing.md));
  and **first-class `DateOnly`/`TimeOnly` mapping** (materializer reads, `DbType.Date`/`Time`
  stamping, `CREATE TABLE` column types on all five dialects, Oracle `TimeOnly` as
  `INTERVAL DAY(0) TO SECOND(7)`), plus the IDE-squiggles troubleshooting docs.
- **Production-readiness round (2026-06-12):** `InquiryOptions.DefaultCommandTimeout` applies a
  global command timeout (explicit `InquiryCommand.CommandTimeout` still wins); an ASP.NET Core
  health check (`AddHealthChecks().AddInquiry()`) opens a connection through the registered
  factory; the sample app wires up `AddInquiryTelemetry()`, `Inquiry.Command` debug logging, and
  a `/health` endpoint; and the **NativeAOT story is verified in CI** — the runtime packages are
  marked `IsAotCompatible` (the assembly-scanning `AddInquiry(Assembly[])` overload and the
  reflection-based `InquiryJsonConverter` are annotated `RequiresUnreferencedCode`; use
  `AddInquiryGeneratedStores()` and source-generated JSON converters under AOT), and a new
  `samples/Inquiry.AotSmoke` app is published as a native binary and executed by the `aot-smoke`
  CI job.
- **Allocation micro-optimizations (2026-06-12):** generated materializers/binders read value converters
  through a shared cached instance instead of allocating one per column per row; streaming filtered
  selects and streaming full-text search use a new allocation-free `QueryAsync<T, TArgs, TMaterializer>`
  fast path (no `InquiryParameter[]`/`InquiryCommand` per call), matching the buffered overloads; batch
  update splices the row index between pre-split template segments instead of `string.Replace` per row;
  `InquirySql` caches the generated `@p0…@p15` parameter names; retrying provider factories no longer
  allocate an open-delegate per connection open.
- **Observability (2026-06-11):** opt-in `AddInquiryTelemetry()` emits OpenTelemetry-compatible spans
  (`ActivitySource` "Inquiry", db semantic conventions), a `db.client.operation.duration` histogram
  (`Meter` "Inquiry"), and `ILogger` messages on the `Inquiry.Command` category. Parameter values are
  never recorded; command text is redactable. Zero pipeline overhead when not registered. See
  [Observability](../articles/features/observability.md).
- **Backup-server failover (2026-06-11):** SQL Server, PostgreSQL, MySQL, and Oracle provider options
  accept a `FailoverConnectionString`; the factory falls back to the backup server when the primary
  fails to open (after any configured retry), per open, with both faults surfaced if both fail. See
  [Resiliency & failover](../articles/features/resiliency.md).
- **Build / runtime floor:** dropped EOL net6.0/net7.0 (now net8.0/net9.0/net10.0; provider runtimes
  net8.0); upgraded all four provider DB clients (Microsoft.Data.SqlClient 7.0.1, Npgsql 10.0.3,
  MySqlConnector 2.6.0, Oracle.ManagedDataAccess.Core 23.26.200) and Testcontainers 3 → 4.12.
- **Correctness:** closed-transaction handles now throw instead of silently using the non-transactional
  pipeline (the leaky `IInquiryTransaction.Inquiry` property was removed); eager-relation SQL constants
  dedupe by relation property, so two relations to the same child type both emit; the MySQL
  `UseDatabaseDefault` upsert update-branch binds the entity value; `QuerySingleOrDefaultAsync` no longer
  requests `SingleRow` while detecting duplicate rows; pagination arguments are validated
  (`offset >= 0`, `limit`/`pageSize > 0`, `pageSize < int.MaxValue`); malformed `OrderBy` directions are
  diagnosed (`INQ042`); projections are allowed on soft-delete entities and compose the active-row filter
  (`INQ027` retired).
- **Upsert atomicity & generated-key parity (all relational engines except Oracle):** generated-key upserts
  are atomic — SQL Server uses `MERGE … WITH (HOLDLOCK)` (client and generated key), PostgreSQL uses
  `INSERT … ON CONFLICT` — so concurrent same-key upserts no longer throw a spurious duplicate-key error;
  covered by live concurrency + `uniqueidentifier`/`gen_random_uuid()` key tests. SQLite + MySQL parity is
  now **test-proven** (live generate + concurrency tests). MySQL additionally supports a **database-generated
  GUID key**: a `Guid?` `UseDatabaseDefault` key is generated server-side via `UUID()` (captured in a
  `@_inquiry_genkey` user variable for the emulated returning), so Inquiry enables `AllowUserVariables=true`
  on MySQL connections by default. (Oracle generated-key upsert remains unsupported, tracked separately.)
- **Providers:** Oracle ref-cursor detection requires the generated `:rc` bind, so it no longer
  misclassifies ad-hoc PL/SQL.
- **Dependency injection:** generated `AddInquiryGeneratedStores()` registration is explicit, so
  `AddInquiry()` no longer scans loaded AppDomain assemblies by default. The
  `AddInquiry(params Assembly[])` fallback remains for intentional assembly-based registration, and
  registering two providers in one container now fails fast with a clear message.
- **Hardening:** sample DB credentials are labeled local-dev-only with an `INQUIRY_SAMPLE_DB` override;
  the known build-warning sources are scoped-suppressed (production projects are warnings-as-errors).
- **CI:** Oracle moved into the integration matrix (net8.0/net9.0); CI emits TRX artifacts.
- **CI hardening:** a provider suite that can't start its Docker container now FAILS CI (via the
  `INQUIRY_REQUIRE_DOCKER` guard) instead of silently skipping; a new scheduled weekly workflow runs the
  full provider × net8.0/net9.0/net10.0 matrix (the normal integration matrix stays net8.0/net9.0).
- **Formal security scan:** the Codex Security repository scan completed during pre-release hardening.
  Findings were fixed with regression coverage for lazy batch parameter-cap enforcement, MySQL
  update-returning concurrency behavior, and Oracle generated bind-name collisions.
- **Prepared-statement benchmark:** the PostgreSQL BenchmarkDotNet harness compares
  `PreparedStatementMode.None` vs `Auto` on Npgsql for a generated simple point read and a stable ad-hoc
  multi-join point read. The 2026-06-06 full run measured lower means for `Auto` in both categories
  (multi-join: 713.5 us vs 944.5 us; simple point read: 587.9 us vs 662.6 us), with BDN distribution
  warnings appropriate for networked container benchmarks.
- **Generator robustness:** a mistyped collection-relation foreign key on a store with no eager method no
  longer crashes the generator (`NullReferenceException`) — relation SELECT consts are emitted only when a
  valid eager method consumes them; a bad relation that *is* eager-loaded still reports `INQ040`/`INQ041`.
- **Pre-release API hardening:** high-level ad-hoc SQL APIs now use safe `FormattableString`
  overloads instead of raw `string` command text, with `InquiryCommand` left as the explicit
  advanced escape hatch. `IInquiry.ExecuteInTransactionAsync(...)` now owns the common
  begin/commit/rollback transaction flow. Runtime implementation types, provider connection factories,
  retry helpers, and request pipelines are internal, and generated-code-only support contracts are
  hidden from IntelliSense where they must remain public for source generation.
