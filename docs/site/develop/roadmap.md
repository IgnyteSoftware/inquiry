# Roadmap

> Inquiry's first stable release will be **1.0.0**; `1.0.0-preview` packages are published on
> nuget.org. Package versioning is independent of the .NET 8 runtime floor.
>
> Inquiry is the **compile-time .NET micro-ORM**: generated constant SQL, binders, and materializers;
> predictable allocations; NativeAOT support; and explicit, validated SQL escape hatches. It is not
> trying to become a stateful ORM with change tracking or a runtime LINQ provider.

## Live status and priorities

**GitHub is the single source of truth for status, priority, and acceptance criteria.** This page no
longer keeps open/closed counts or a priority table — those drift the moment an issue moves. Read them
live instead:

- [`1.0.0` milestone](https://github.com/IgnyteSoftware/inquiry/milestone/1) — overall progress toward the first stable release.
- [Open **P1** — must complete for 1.0](https://github.com/IgnyteSoftware/inquiry/issues?q=is%3Aissue+is%3Aopen+milestone%3A1.0.0+label%3A%22priority%3AP1%22).
- [Open **P2** — target for 1.0 if the release gates stay healthy](https://github.com/IgnyteSoftware/inquiry/issues?q=is%3Aissue+is%3Aopen+milestone%3A1.0.0+label%3A%22priority%3AP2%22).
- [All open `1.0.0` issues](https://github.com/IgnyteSoftware/inquiry/issues?q=is%3Aissue+is%3Aopen+milestone%3A1.0.0).
- [`1.1.0` milestone](https://github.com/IgnyteSoftware/inquiry/milestone/2) — additive work deferred until after the API freeze.
- [Post-1.0 / demand-driven](https://github.com/IgnyteSoftware/inquiry/labels/roadmap) work carries the `roadmap` label.

Shipped history lives in [`CHANGELOG.md`](https://github.com/IgnyteSoftware/inquiry/blob/main/CHANGELOG.md)
and the [closed `1.0.0` issues](https://github.com/IgnyteSoftware/inquiry/issues?q=is%3Aissue+is%3Aclosed+milestone%3A1.0.0).

The rest of this page is the **durable** material a tracker holds poorly: what "feature complete" means,
the release gates, the design rationale behind unshipped work, and what is deliberately out of scope.

## Product contract

For 1.0, "feature complete" means:

- Production-grade CRUD, static querying, relationships, transactions, bulk/batch operations,
  observability, resiliency, testing, and schema workflows.
- Generated SQL remains statically shaped, provider-correct, parameterized, and NativeAOT-compatible.
- Unsupported provider capabilities fail clearly at build time rather than through generated throwing stubs.
- Complex workloads can use validated ad-hoc SQL without leaving Inquiry's materialization, transaction,
  telemetry, and testing surfaces.
- Performance claims are workload-scoped and backed by reproducible current benchmarks; raw ADO.NET is
  treated as the implementation floor.

## 1.0 release gates

1. **Correctness:** all unit, generator, SQLite, and live provider suites pass on every supported TFM;
   transaction, cancellation, concurrency, schema, batch, bulk, and failover behavior have live coverage.
2. **Public contract:** public API/APICompat baseline, analyzer diagnostic release tracking, provider
   capability diagnostics, and a reviewed compatibility policy are checked in.
3. **Performance:** corrected baselines and current competitors cover CRUD, streaming, eager/M:N,
   collections, paging, batch/bulk, transactions, cold/warm startup, and concurrency; stable regression
   budgets guard latency and allocations.
4. **Packaging:** clean projects consume the actual eleven `.nupkg` files on .NET 8/9/10 and NativeAOT;
   SourceLink, symbols, metadata, icon, provenance, and dependency/security evidence are verified.
5. **Documentation and governance:** hosted versioned docs match generated behavior, the tracker and roadmap
   agree, release/support/security policies are published, and required review/status checks protect releases.

## Detailed feature backlog

The sections below preserve research and implementation context. GitHub issue acceptance criteria and
the [live status](#live-status-and-priorities) above supersede any older wording that describes an
initial implementation as fully complete.

- **Aspire integration package (implemented 2026-08-29).** The `Inquiry.Aspire` client
  integration in the standard shape every mainstream data library now ships: resolve the
  Aspire-provisioned connection string by resource name, register the provider factory, and
  auto-wire the existing telemetry (`AddInquiryTelemetry()`) and health check so Inquiry lights up
  the Aspire dashboard. Foundation work: build provider connection factories on
  **`System.Data.Common.DbDataSource`** (the .NET 7+ pooled primitive Aspire registers) instead of
  raw connection strings. *(Foundation started 2026-06-21: `PostgreSqlInquiryConnectionFactory` now
  builds and owns one app-lifetime `NpgsqlDataSource` — a `DbDataSource` — in #54/PR #99.
    MySQL and MariaDB refactored to `MySqlDataSource` on 2026-07-09. External data-source overloads
    now accept those three provider types. SQL Server accepts the generic `DbDataSource` returned by
    `SqlClientFactory.CreateDataSource`; SQLite and Oracle use the default `DbDataSource`
    implementations returned by their provider factories.)* See
  [Aspire integration](../articles/features/aspire.md).
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
- **`dotnet new` project templates** *(integration research 2026-06-12)*. An Aspire-ready starter
  template with a provider, telemetry, health checks, and tests wired from the first build.
- **First-party provider authoring kit and conformance suite**
  ([#184](https://github.com/IgnyteSoftware/inquiry/issues/184)). Replace the current copy-an-existing-
  provider workflow with a scaffold, shared analyzer-pack/test MSBuild plumbing, reusable provider
  registration and resilient-open composition, and source-linked cross-provider contract tests. Generated
  stores must remain source-compiled inside each provider test assembly so every dialect analyzer is
  exercised. Inquiry 1.0 supports only the six first-party providers already in this repository. The
  authoring kit and stable public provider SDK are tracked for 1.1.
- **Default interceptor library — remaining scope** *(gap research 2026-06-12)*. The
  `Inquiry.Interceptors` package shipped with slow-query warning logging and sqlcommenter
  trace-context tagging (see the [changelog](https://github.com/IgnyteSoftware/inquiry/blob/main/CHANGELOG.md)); the command-text assertion
  interceptor already lives in `Inquiry.Testing`. Remaining: audit trail (who/when/what changed —
  XPO's module as an interceptor; pairs with `[InquiryModifiedBy]`), DataAnnotations entity
  validation before insert/update (needs an entity-level seam — the command interceptor sees SQL,
  not entities), and the N+1 detector (see *Dev-time query diagnostics* above).
- **Read-replica routing** *(gap research 2026-06-12)*. Route SELECTs to a read-replica pool and pin
  mutations + transactions to the primary (Drizzle `withReplicas` / Sequelize / TypeORM semantics).
  No mainstream .NET ORM ships this; Inquiry already has the connection-factory and failover chassis
  to build on.
- **Database-first scaffolding CLI** *(gap research 2026-06-12)*. A `dotnet inquiry scaffold` tool
  that introspects an existing database and emits attributed entities + store skeletons — the
  `dotnet ef dbcontext scaffold` / `prisma db pull` / `drizzle-kit pull` workflow. Largest effort,
  largest onboarding lever for existing databases.
- **Many-to-many relations — writing through an auto-managed junction** *(gap research 2026-06-12,
  narrowed 2026-08-01)*. Eager-loading M:N shipped for all three shapes — an explicitly-mapped junction,
  a composite-key related entity, and an auto-managed junction Inquiry synthesizes — see the
  [changelog](https://github.com/IgnyteSoftware/inquiry/blob/main/CHANGELOG.md). Remaining: an auto-managed junction is **read-only**, because
  inserting or deleting a link row needs store methods the junction has no CLR type for. Writing links
  means mapping the junction explicitly, or raw SQL against the generated table.
- **CTEs and set operations** *(gap research 2026-06-12)*. `WITH` / `UNION` / `INTERSECT` / `EXCEPT`
  composition in the predicate/select model (Kysely-style); ad-hoc SQL covers this today.
- **Provider-specific column types** *(gap research 2026-06-12)*. SQL Server **vector** columns first
  (EF 10 `SqlVector` parity — AI embeddings / semantic search); spatial and `hierarchyid` by demand.
- **Additional database engines** *(gap research 2026-06-12)*. XPO supports 15+ engines vs Inquiry's 6;
  add engines (Firebird, DB2, …) demand-driven — the provider + analyzer split makes
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
  six engines; replicate the full Northwind entity/relationship surface (all tables, all CRUD + read
  shapes) across ADO.NET / Inquiry / Dapper / EF Core in both tests and benchmarks, so every feature is
  compared apples-to-apples on every entity.
- **Multi-database in one container.** Inquiry binds a single global `IInquiryConnectionFactory` per
  service collection (now enforced — registering two providers throws a clear exception). True
  multi-provider support would require keyed/named factories or per-provider store scopes — .NET 8
  keyed DI services are the natural mechanism *(integration research 2026-06-12)*.
- **Optional Roslyn bump.** `Microsoft.CodeAnalysis.CSharp` is intentionally held at 4.8.0 to keep the
  analyzer's minimum-SDK floor low; revisit only if a newer Roslyn API is needed.

## Explicitly out of scope for 1.0

- **PostgreSQL PG17 MERGE…RETURNING for generated-key upsert (#60).** Closed — the existing dual-CTE
  `INSERT … ON CONFLICT` approach is correct, performant, and avoids the PG17 minimum-version gate.
  MERGE adds no benefit here.
- **Migrations Phase B** (schema diff / `ALTER` / versioning) — delegate to DbUp or FluentMigrator;
  Inquiry emits initial `CREATE TABLE` DDL only (`InquiryGeneratedSchema.Ddl`).
- **NoSQL / document engines** (Cosmos DB, MongoDB) — they don't fit a SQL-generating, schema-bound,
  JOIN/eager-loading model.
- **Lazy loading and transparent graph tracking** — explicit generated eager methods remain the contract;
  provider-specific JOIN shapes may be considered later only when benchmarks justify them.
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
