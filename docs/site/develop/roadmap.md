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

> The four items below came out of the 2026-06-12 competitive feature-gap research (vs EF Core, XPO,
> Dapper + ecosystem, and the JS/TS ORMs) and are ordered by expected impact.

- **`DbBatch` pipeline support.** Adopt the ADO.NET batching API
  (`System.Data.Common.DbBatch`, .NET 6+; supported by Npgsql, SqlClient, MySqlConnector) so
  multi-command operations run in one round trip. Cleaner than today's concatenated multi-statement
  batch-update text, and could unlock batch `UpdateAll` on **Oracle** (currently a throwing stub —
  Oracle has no portable multi-statement text form). Only Dapper.AOT exposes this today; no mainstream
  .NET ORM does.
- **Provider-native bulk copy.** A `BulkInsertAsync` tier riding `SqlBulkCopy`, Npgsql binary `COPY`,
  and `MySqlBulkCopy` (the Dapper Plus / linq2db class of operation). Inquiry's multi-row `VALUES`
  batch insert is parameter-capped (~2k parameters); bulk copy is the 100k+-row tier. Falls back to
  the existing batch SQL where a provider has no bulk-copy API.
- **Array parameters for `IN` + table-valued parameters.** `Compare.In` predicates rewrite the command
  text per list cardinality, which defeats prepared-statement reuse across list lengths. PostgreSQL
  `= ANY(@ids)` (and equivalents) would keep the SQL constant; SQL Server TVPs are the sibling
  mechanism for passing sets to commands and stored procedures.
- **Single-round-trip eager loading.** Separate-query eager loading currently pays one round trip per
  relation; combining the parent + relation SELECTs into one multi-result-set command (Dapper
  `QueryMultiple`-style) keeps the design but cuts the latency to one round trip.

## Planned features & enhancements

> Items marked *(gap research 2026-06-12)* came out of the competitive feature-gap analysis vs
> EF Core, XPO, Dapper + ecosystem, and the JS/TS ORMs (Prisma, Drizzle, TypeORM, Sequelize, Kysely).

- **Set-based predicate mutations** *(gap research 2026-06-12)*. `ExecuteUpdate`/`ExecuteDelete`-style
  operations — UPDATE/DELETE by WHERE predicate without loading entities (e.g. `[InquiryUpdateWhere]`,
  `[InquiryDeleteWhere]`), reusing the existing compile-time predicate model. The most-missed everyday
  feature relative to EF Core.
- **Default interceptor library** *(gap research 2026-06-12)*. A companion package (e.g.
  `Inquiry.Interceptors`) of ready-made `IInquiryCommandInterceptor` implementations: audit trail
  (who/when/what changed — XPO's module as an interceptor), sqlcommenter-style trace-context SQL
  comments / query tagging for DBA correlation (no .NET ORM ships sqlcommenter today), slow-query
  warning logging, and a command-text assertion interceptor for tests. Keeps the core dependency-free
  while making the interceptor seam batteries-included.
- **Read-replica routing** *(gap research 2026-06-12)*. Route SELECTs to a read-replica pool and pin
  mutations + transactions to the primary (Drizzle `withReplicas` / Sequelize / TypeORM semantics).
  No mainstream .NET ORM ships this; Inquiry already has the connection-factory and failover chassis
  to build on.
- **Stored-procedure output/return parameters** *(gap research 2026-06-12)*. Surface
  `ParameterDirection.Output`/`ReturnValue` on generated stored-procedure methods, completing the
  sproc story (Dapper `DynamicParameters` parity).
- **Database-first scaffolding CLI** *(gap research 2026-06-12)*. A `dotnet inquiry scaffold` tool
  that introspects an existing database and emits attributed entities + store skeletons — the
  `dotnet ef dbcontext scaffold` / `prisma db pull` / `drizzle-kit pull` workflow. Largest effort,
  largest onboarding lever for existing databases.
- **View-mapped / keyless read-only entities** *(gap research 2026-06-12)*. Map a read-only store
  over a database view or keyless projection (EF keyless entities / TypeORM `@ViewEntity`).
- **Server-computed columns** *(gap research 2026-06-12)*. Computed-column DDL + materializer support
  for properties calculated by the database (EF `HasComputedColumnSql`, XPO persistent aliases).
- **Many-to-many relations** *(gap research 2026-06-12)*. Auto-managed junction tables for M:N
  associations; the relation model is currently 1:N / N:1 only.
- **CTEs and set operations** *(gap research 2026-06-12)*. `WITH` / `UNION` / `INTERSECT` / `EXCEPT`
  composition in the predicate/select model (Kysely-style); ad-hoc SQL covers this today.
- **Tenant/global query filters + Postgres RLS helpers** *(gap research 2026-06-12)*. Generalize the
  soft-delete global-filter machinery to user-defined columns (EF `HasQueryFilter` / EF 10 named
  filters), plus row-level-security session helpers for PostgreSQL (Drizzle RLS-style).
- **Data-seeding convention** *(gap research 2026-06-12)*. A thin first-class seeding hook
  (EF `UseSeeding` / `prisma db seed` analog) formalizing the sample's `DataSeeder` pattern.
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
  multi-provider support would require keyed/named factories or per-provider store scopes.
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

## Recently resolved

Since the 2026-06-03 internal review, the following were fixed (each with regression tests) and are **not**
open:

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
