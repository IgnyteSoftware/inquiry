# Roadmap

> This page lists **open** work only — known issues, security follow-ups, performance ideas, and planned
> enhancements. Resolved items are summarized at the [bottom](#recently-resolved). Nothing here blocks
> `main`: the library builds and every test suite passes.
>
> **Last reconciled against the code:** 2026-07-08.

## Known issues & correctness

- **PostgreSQL bulk COPY writes untyped values (#122).** The binary copier uses `NpgsqlBinaryImporter`
  but calls the untyped `WriteAsync(value)` overload with no explicit `NpgsqlDbType`. Npgsql's type
  inference fails on `timestamptz` columns (when `DateTime.Kind` is not UTC), `jsonb`/`json` columns
  (value-converter-produced strings), and native PostgreSQL enum columns. Fix: thread the destination
  column's `NpgsqlDbType` through `InquiryBulkInsertDefinition` and use the typed `WriteAsync` overload.
- **Bulk copiers hard-cast `DbConnection` to concrete provider type (#159).** SQL Server, PostgreSQL,
  and MySQL bulk copiers all direct-cast `DbConnection` → `SqlConnection`/`NpgsqlConnection`/
  `MySqlConnection`. A profiling wrapper (MiniProfiler) or decorated connection throws
  `InvalidCastException` with no actionable message. Fix: type-test with `is` and throw a clear error.
- **Cross-dialect consistency gaps (#157).** Oracle defaults decimal to `(19,4)` vs `(18,2)` everywhere
  else; MySQL empty-SET upsert-returning returns the matched row while others return null; Oracle has no
  `CREATE TABLE IF NOT EXISTS` guard (`ORA-00955`); Oracle MERGE upsert race condition is undocumented.
- **SqlServer `AccessTokenProvider` used for failover server (#130).** The token provider is applied to
  both primary and failover connections. If the backup is a different Entra tenant/resource, the primary's
  token is wrong. Same-tenant AG replicas (the common case) are fine. Fix: document the assumption or let
  the callback distinguish targets.
- **Analyzer DLL pack paths hardcode bin directory (#159).** The provider `.csproj` files reference
  analyzer DLLs via `bin\$(Configuration)\netstandard2.0\` — the `bin\` prefix and TFM are hardcoded
  strings rather than property-driven paths, breaking under `UseArtifactsOutput` or custom `OutputPath`.

## Security

- *No open security follow-ups are currently known.* The formal Codex Security repository scan has been run;
  validated findings were fixed in `318ee5f` and summarized in [Security](../articles/security.md).

## Performance & optimization

> The items below came out of the 2026-06-12 competitive feature-gap research (vs EF Core, XPO,
> Dapper + ecosystem, and the JS/TS ORMs) and are ordered by expected impact. (`DbBatch` pipeline
> support shipped — see [Recently resolved](#recently-resolved).)

- **Table-valued parameters (SQL Server) (#69).** PostgreSQL array `IN` parameters shipped (see
  [Recently resolved](#recently-resolved)); SQL Server TVPs remain the sibling mechanism for passing
  sets to commands and stored procedures on that engine.
- **Single-round-trip eager loading (#70).** Combining the parent + relation SELECTs into one
  multi-result-set command (Dapper `QueryMultiple`-style) keeps the separate-query design but cuts the
  latency to one round trip. Shipped for both eager shapes on the four dialects that can multiplex
  `;`-separated result sets (SQLite/PostgreSQL/MySQL/SQL Server) — `[InquirySelectOneByKeyEager]` and now
  `[InquirySelectAllEager]` (#70/PR #118) read a single command through an `InquiryGridReader`.
  **Remaining:** Oracle, which cannot return multiple result sets from one command (ORA-00933) and keeps
  the per-relation path until PL/SQL ref-cursor batching is added.
- **MariaDB-native INSERT/DELETE RETURNING (#58).** MariaDB 10.5+ supports `INSERT…RETURNING` and
  `DELETE…RETURNING` natively (halving round trips), but the MySQL builder targets the 5.7 LCD.
  A compatibility flag or MariaDB dialect would enable the native path and eliminate the
  `AllowUserVariables` dependency for GUID keys.

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
  raw connection strings. *(Foundation started 2026-06-21: `PostgreSqlInquiryConnectionFactory` now
  builds and owns one app-lifetime `NpgsqlDataSource` — a `DbDataSource` — in #54/PR #99. Remaining:
  refactor the SQL Server, MySQL, SQLite, and Oracle factories to their native `DbDataSource` builders.)*
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
- **DDL safety lint — more rules** *(integration research 2026-06-12)*. The opt-in lint surface shipped
  with INQ061 (unindexed foreign key), INQ062 (decimal without precision/scale), and INQ064 (unindexed
  filtered column) — see [Recently resolved](#recently-resolved). squawk-inspired follow-ups: nullable
  column with a default, oversized/unbounded text columns.
- **Testing follow-ups: transaction sandbox + data factories** *(adoption review 2026-06-12)*.
  The store interfaces and the `Inquiry.Testing` package (SQLite fixture, recording interceptor,
  Respawn reset) shipped — see [Recently resolved](#recently-resolved) and
  [Testing](../articles/features/testing.md). Remaining scope: an **Ecto-style SQL sandbox**
  (each test inside a rolled-back transaction with connection ownership, enabling parallel
  database tests) and **factory_bot/Laravel-style test-data factories** (states/sequences,
  Bogus-compatible).
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
- **Many-to-many relations — auto-managed junction** *(gap research 2026-06-12)*. Eager-loading M:N
  through an explicitly-mapped junction shipped — see [Recently resolved](#recently-resolved). Remaining:
  an *auto-managed* / implicit junction table (no hand-written junction entity), composite-key related
  entities, and applying the child's soft-delete/global filters to the eager M:N collection.
- **CTEs and set operations** *(gap research 2026-06-12)*. `WITH` / `UNION` / `INTERSECT` / `EXCEPT`
  composition in the predicate/select model (Kysely-style); ad-hoc SQL covers this today.
- **Parameterized & named query filters + Postgres RLS helpers** *(gap research 2026-06-12)*. The
  static-column form shipped — see [Recently resolved](#recently-resolved). The remaining gaps are
  runtime-parameterized filters (a tenant id bound from ambient context rather than a constant column),
  EF 10 *named* filters (selectively ignore one filter by name), optional **write-side enforcement** so
  the filter also guards key-based UPDATE/DELETE (today it is read-side only, like soft delete and EF
  `HasQueryFilter` — see the [Global query filters](../articles/features/global-filters.md) scope note),
  and row-level-security session helpers for PostgreSQL (Drizzle RLS-style `SET LOCAL` around the connection).
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
- **Optional Roslyn bump.** `Microsoft.CodeAnalysis.CSharp` is intentionally held at 4.8.0 to keep the
  analyzer's minimum-SDK floor low; revisit only if a newer Roslyn API is needed.
- **Telemetry enrichment (#86).** The opt-in telemetry layer (see
  [Observability](../articles/features/observability.md)) emits OTel-conventional spans, a
  `db.client.operation.duration` histogram, and `ILogger` messages. Candidate follow-ups:
  a `db.collection.name` (table) span tag, sqlcommenter-style trace-context SQL comments, and
  connection-open / pool-wait instruments.
- **DISTINCT support on select/projection read shapes (#66).** A `Distinct = true` knob on
  `[InquirySelectAll]` and the projection attribute rendering `SELECT DISTINCT` in the const SQL.
  Most valuable on column-subset projections (distinct `Country` values). Currently requires an ad-hoc
  SQL escape.
- **Grouped aggregate read shape (#65).** A `[InquiryGroupCount]` / `[InquiryGroupAggregate]` attribute
  emitting `SELECT <groupCol>, COUNT(*) FROM t GROUP BY <groupCol>` — the "counts by status" / "orders
  per customer" dashboard primitive. Today it requires hand-written SQL + `[InquiryAdHoc]`.
- **Top-1-by-order read shape (#64).** A `[InquirySelectTopByOrder]` attribute returning `Task<T?>` —
  the row with the extreme value of a column (`SELECT … ORDER BY col LIMIT 1`). EF Core 11
  `MaxByAsync`/`MinByAsync` parity.

## Test coverage & hardening

- **Port SQLite-only integration tests to server dialects (#154).** ManyToMany, GlobalFilter, audit
  columns, ComputedColumn, JsonPath, and InquiryView have live tests only on SQLite. GlobalFilter is
  highest priority (tenant isolation — a dialect-specific bug is a security issue).
- **Oracle has zero test coverage for `[InquiryBulkInsert]` (#155).** Oracle's bulk insert path (which
  compiles down to multi-row batch insert) is completely unverified.
- **CancellationToken propagation never verified against real MySQL/PostgreSQL (#156).** Pipeline-level
  unit tests thread the token through the API surface, but no integration test verifies that a cancelled
  token actually cancels an in-flight database operation on these providers.
- **Single-row all-types bulk-insert test matrix (#134).** No test covers bulk insert of every
  provider-primitive type (int, decimal, bool, Guid, DateTime, string, byte[], enum, converter columns)
  in a minimal batch per bulk-copy provider.
- **Guard Oracle `:rc` ref-cursor finalize-once invariant (#136).** `FinalizeCommand` unconditionally
  adds the `:rc` OUT ref-cursor parameter. A second finalize of the same command would bind a duplicate.
  Not a live bug (every call site creates a fresh command), but an unstated invariant a cheap guard would
  remove.
- **Generator polish (#135).** Analyzer release tracking is suppressed (`RS2008`); the diagnostic-ID
  registry comment implies INQ038 exists (it's only reserved); `ProjectionProcessor.Extract` and
  `AdHocProcessor.Extract` take no `CancellationToken`.

## Documentation gaps

- **MySQL `AllowUserVariables` makes misspelled raw-SQL parameters silently NULL (#133).** The forced
  `AllowUserVariables=true` means a misspelled `@param` in user SQL is treated as a NULL user variable
  instead of throwing.
- **Per-provider capability table for retry/failover knobs (#132).** Retry options exist only on SQL
  Server and PostgreSQL; MySQL and Oracle expose failover only. The top-level docs imply parity.
- **SqlServer `SqlBulkCopy` defaults (#131).** `BulkCopyTimeout` stays at its 30-second default (a
  footgun on large loads); no `TableLock` option is exposed, so bulk inserts take row locks and aren't
  minimally logged.

### Explicitly not planned

- **PostgreSQL PG17 MERGE…RETURNING for generated-key upsert (#60).** Closed — the existing dual-CTE
  `INSERT … ON CONFLICT` approach is correct, performant, and avoids the PG17 minimum-version gate.
  MERGE adds no benefit here.
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

- **SQL Server MERGE upsert replaced with update-first pattern (#59, 2026-07-08).** All MERGE-based upsert
  SQL replaced with `UPDATE … IF @@ROWCOUNT = 0 INSERT` wrapped in `BEGIN/COMMIT TRANSACTION` with
  `UPDLOCK, SERIALIZABLE` table hints. Eliminates MERGE's plan-cache bloat and deadlock risks while
  maintaining atomicity via key-range locks. All three upsert variants (standard, empty-SET, generated-key)
  converted; concurrency-tested.
- **SelectAllEager child table full scan eliminated (#57, 2026-07-08).** The `_All` and M:N `_Junction`
  eager-loading consts now use a subquery filter (`WHERE fk IN (SELECT pk FROM parent)`) to scope child
  rows to the parent result set at the SQL level — no runtime parameters needed, preserves the grid path.
  Turns an O(child-table) scan into an indexed range read on all five dialects.

- **Plan-caching follow-ups + eager/cache perf (2026-06-22):** the cluster's tracked follow-ups plus two
  generator/perf items, each with tests.
  - **Eager-load key `Size`/`Precision` (#107/PR #109)** and **IN-list element `Size`/`Precision`
    (#102/PR #110):** extend the #56 SQL Server plan-cache `Size`/`Precision` emission to the eager-load key
    binders and to `Compare.In`/`NotIn` list elements (live DMV-verified). **Batch-keys IN `Size`
    (#112/PR #115)** closes the last expansion call site.
  - **`[InquiryColumn]` range validation `INQ065` (#103/PR #111):** rejects out-of-range
    `Length`/`Precision`/`Scale`, and SQL Server DDL maps an over-fixed-width `Length` to a MAX type instead
    of an illegal `NVARCHAR(5000)`. Extended to a dialect-aware ceiling so an over-ceiling string key/indexed
    column is diagnosed (`INQ031`/`INQ032`) and Oracle/MySQL map to `CLOB`/`LONGTEXT` (#113/PR #116).
  - **Live cross-dialect bucket-boundary + NOT IN coverage (#106/PR #114)** and **SQL Server plan-cache
    benchmarks (#105/PR #117)** for IN-bucketing (#67) and parameter `Size` (#56).
  - **Single-round-trip `SelectAllEager` (#70/PR #118):** see Performance above.
  - **`LocationData` cache key drops absolute `TextSpan` (#62):** editing text above an entity on an existing
    line no longer busts its incremental-generator cache (cache-tracking test). A newline insert above still
    invalidates (it shifts line numbers); decoupling diagnostic reporting from source emit to cache that too
    is a tracked follow-up.
  - **`QueryListAsync` capacity hints (#61):** the buffered read path takes an optional `capacityHint`; the
    generated offset-paged reader passes the `limit` and the keyset reader passes `pageSize + 1`, so the
    result list is pre-sized instead of grown-and-copied. The keyset over-fetch trim is now a single in-place
    `RemoveAt` (no second list, no per-item copy).
- **Plan-caching cluster (2026-06-21/22):** four interrelated items closed.
  - **PostgreSQL single `NpgsqlDataSource` (#54/PR #99):** `PostgreSqlInquiryConnectionFactory` builds one
    app-lifetime `NpgsqlDataSource` in its constructor (Npgsql's recommended model since 6.0) and implements
    `IAsyncDisposable` to drain the pool with the container; a failover string gets its own data source. Also
    the `DbDataSource` foundation for the Aspire item above.
  - **Cross-dialect statement-cache story (#55/PR #100, docs-only):** measured and documented per-provider
    behavior — Oracle's ODP.NET self-tuning already caches by default (Inquiry sets nothing), SQL Server
    relies on the `sp_executesql` plan cache, MySQL's `IgnorePrepare=false` is already correct. The original
    "enable Oracle statement caching" premise was empirically false. See
    [Prepared statements](../articles/features/prepared-statements.md).
  - **SQL Server parameter `Size`/`Precision` (#56/PR #101):** generated binders emit `Size` (declared-length
    string) and `Precision`/`Scale` (declared decimal) on **predicate** parameters (never write binders,
    where `Size` would truncate), keeping the `sp_executesql` plan-cache signature stable across value
    lengths. SqlServer-only; other dialects unchanged. Proven by a live DMV test.
  - **IN-list bucketing (#67/PR #104):** `Compare.In`/`NotIn` pads the expanded list to the next power-of-two
    length (repeating an element), bounding distinct cached plans to ~`log2` of the parameter limit on
    SqlServer/MySql/SQLite/Oracle (PostgreSQL uses `= ANY` and is unaffected). Capped at Oracle's 1000-entry
    `ORA-01795` limit. Proven by live SQL Server (9 cardinalities → 5 signatures) and Oracle (600-element)
    DMV/round-trip tests.
  - Follow-ups tracked: #102 (Size on IN-list elements), #103 (validate `[InquiryColumn]` Length/Precision/
    Scale ranges), plus the test/benchmark coverage issues filed alongside.

- **Column-encryption docs (2026-06-13):** application-side column encryption needs no bespoke API — it
  rides the existing [value-converter](../articles/features/value-converters.md) seam (encrypt in
  `ToProvider`, decrypt in `FromProvider`). New [Column encryption](../articles/features/column-encryption.md)
  article documents the pattern with a worked `AesGcm` converter (key from a static holder, since
  converters are stateless/parameterless), the query trade-off (no filtering/indexing on the ciphertext;
  use a keyed-HMAC lookup column), and the SQL Server Always Encrypted / PostgreSQL pgcrypto native
  alternatives. Proven end-to-end by a live SQLite test (`ColumnEncryptionIntegrationTests`): the property
  round-trips while the column holds ciphertext, never the plaintext.

- **DDL safety lint — unindexed filtered column `INQ064` (2026-06-13):** a third opt-in lint (off by
  default, like INQ061/062). A non-key column a store method filters on — a `[InquirySelectAllByField]`
  field or an `[InquiryWhere]` criterion — with no index makes those queries scan; the lint collects every
  filtered column and flags the un-indexed ones (suggesting `[InquiryColumn(IsIndexed = true)]`). The
  squawk-inspired "missing index on a frequently-filtered column" rule. Generator tests (field + predicate
  filters fire, indexed column doesn't, off without opt-in). See
  [Schema DDL](../articles/features/schema-ddl.md#ddl-safety-lints-opt-in).

- **Existence tests `[InquiryExists]` (2026-06-13):** the `EXISTS` / EF `.AnyAsync()` analog — returns
  `Task<bool>` from `SELECT CASE WHEN EXISTS(SELECT 1 FROM … WHERE …) THEN 1 ELSE 0 END`, which
  short-circuits at the first match (cheaper than `COUNT(*) > 0`). Takes zero or more `[InquiryWhere]`
  criteria (reusing the predicate-select resolution/binder, including IN/NOT IN expansion); with none it
  tests the whole table. The inner test composes the active-row filter (soft-delete / global filters). The
  CASE form is portable across SQLite/SqlServer/PostgreSQL/MySQL (Oracle appends `FROM DUAL`); the
  resulting 1/0 is coerced to `bool` by the scalar path. Generator snapshots (incl. Oracle FROM DUAL,
  active-row composition) + live SQLite round-trips (whole-table, by-criteria, soft-delete exclusion).

- **Many-to-many relations `[InquiryManyToMany]` (2026-06-13):** eager-loading a M:N association through
  a mapped junction (link) entity. The single-parent eager load (`[InquirySelectOneByKeyEager]`) joins
  the related rows through the junction filtered by the parent key; the all-parents load
  (`[InquirySelectAllEager]`) assembles every parent's collection **in memory from two queries** (all
  children + all junction rows) — no N+1 — reusing the child's and junction's existing materializers. The
  JOIN is ANSI-uniform across all five dialects (space alias for Oracle, table-qualified child columns).
  Misconfiguration (non-collection nav, unmapped junction/child, missing junction FK property, composite
  child key) is **`INQ063`**. Generator snapshots across dialects + live SQLite round-trip (single-eager,
  all-eager, empty collection). Writing associations is through the junction entity's own store. See
  [Many-to-many relations](../articles/features/many-to-many.md).

- **Negated predicate operators `Compare.NotLike` / `Compare.NotBetween` / `Compare.NotIn` (2026-06-13):**
  the negations of the three non-trivial operators — `NotLike` renders `NOT (col LIKE @p)` (reusing the
  LIKE hook so any dialect ESCAPE handling stays consistent), `NotBetween` renders
  `col NOT BETWEEN @lo AND @hi`, and `NotIn` renders `(col NOT IN (sentinel))` expanded at run time by a
  new `InquiryInExpansion.ExpandNotIn` — an empty collection rewrites to `(NULL) OR 1=1` so it matches
  *every* row (the opposite of an empty `IN`), kept self-contained by the wrapping parens. `NotIn` is
  dialect-uniform on the sentinel path (it never uses PostgreSQL's `= ANY` array bind), so the empty case
  behaves identically everywhere. All compose with AND/OR and the active-row filters and share the
  Like/Between/In parameter validation. Generator snapshots (incl. Oracle sigil, PG-uses-sentinel) + live
  SQLite round-trips (incl. empty `NotIn`).

- **DDL safety lints — opt-in surface + first rules (2026-06-13):** advisory analyzer lints for risky
  schema shapes, **off by default** (consumers opt in per ID via `.editorconfig`, so a lint never breaks
  a build until requested). **`INQ061`**: a foreign-key column with no index — most engines don't
  auto-index FKs, so joins and cascades over them scan; dialect-aware (MySQL/InnoDB auto-indexes FK
  constraints and is exempt, unless `GenerateForeignKeys = false`). **`INQ062`**: a decimal column with
  no explicit precision/scale, which takes the dialect default (e.g. `DECIMAL(18,2)`) and can silently
  round (EF's `DecimalTypeDefaultWarning` analog). Generator tests across the auto-indexing /
  non-auto-indexing dialects, indexed/unindexed columns, and explicit-vs-default decimal storage. See
  [Schema DDL](../articles/features/schema-ddl.md#ddl-safety-lints-opt-in).

- **JSON-path predicate querying `[InquiryWhere(JsonPath = …)]` (2026-06-13):** filter inside a JSON
  text column from a predicate method (EF JSON-query parity). A criterion compares the dialect's
  extraction of a `$.a.b` path against the bound parameter — `json_extract` (SQLite), `JSON_VALUE`
  (SqlServer/Oracle), `JSON_UNQUOTE(JSON_EXTRACT(…))` (MySQL), and the `#>>` text-path operator with a
  translated `{a,b}` path (PostgreSQL). It composes with AND/OR, the other operators, and the active-row
  filters like any criterion; the bound parameter name derives from the path leaf (`$.address.city` →
  `@city`). v1 scope: the field must be a plain `string` JSON-text column (no value converter) and
  comparisons are textual — invalid placement / malformed path is **`INQ060`**. Generator snapshots
  across all five dialects + live SQLite round-trip (top-level, nested, composed). See
  [JSON-path querying](../articles/features/json-path-querying.md).
- **Global query filters `[InquiryGlobalFilter]` (2026-06-13):** the EF `HasQueryFilter` parity for a
  static column predicate, generalizing the soft-delete active-row machinery to columns you define
  (multi-tenant isolation, `IsActive`/`IsPublished` gates). A non-nullable `bool` column marked
  `[InquiryGlobalFilter]` makes every generated SELECT (incl. COUNT/aggregate/paged/keyset/projection)
  AND-compose `"col" = <KeepWhen>`; `KeepWhen = false` inverts the kept value; multiple filters
  AND-compose. The condition is baked into the `const` SQL at compile time (zero runtime cost) and uses
  the per-dialect bool literal (PG `TRUE`/`FALSE`, others `1`/`0`). Unlike soft delete there is **no
  per-method opt-out** — a safety boundary shouldn't be bypassable by a stray flag — so on an entity
  with both, `IncludeDeleted = true` drops only the soft-delete term and the global filter survives.
  Invalid placement (non-bool/nullable, or doubling as key/generated/default/soft-delete/token) is
  **`INQ059`**. Generator snapshots across all five dialects + live SQLite round-trip (publish gate,
  soft-delete coexistence, `KeepWhen=false`). See [Global query filters](../articles/features/global-filters.md).
- **Broadened relation-shape diagnostics (2026-06-13):** `INQ040` (unknown relation foreign key)
  and `INQ041` (composite-key child) now report at **declaration time** for every `[InquiryRelation]`,
  so a mistyped relation is caught even when no method eager-loads it (previously silent). New
  **`INQ058`** flags a reversed relation — the foreign-key property found on the opposite side
  (a collection FK belongs to the child, a reference FK to the parent). The eager-emit path no
  longer re-reports (it only drops the bad method), and the relation diagnostics carry the relation
  property's source location. See [Eager loading](../articles/features/eager-loading.md).
- **CI repo-wide warning gate (2026-06-13):** a `tests/Directory.Build.props` (importing the
  root props) sets `TreatWarningsAsErrors` for every test project, so a new warning in test code now
  fails the build like it already does for production projects. The known benchmark warning sources
  live under `benchmarks/` and are unaffected; a clean full-solution build is warning-free.
- **Server-computed columns (2026-06-13):** `[InquiryColumn(Computed = "<expr>")]` maps a
  database-computed column (EF `HasComputedColumnSql` analog) — excluded from generated
  INSERT/UPDATE but selected/materialized and recomputed by the database. The `CREATE TABLE` emits
  each dialect's form (`AS (<expr>)` on SQLite/SqlServer/Oracle via a shared base renderer; typed
  `GENERATED ALWAYS AS (<expr>) STORED` on PostgreSQL/MySQL via overrides). Combining `Computed`
  with a key/default/audit/soft-delete/token column is INQ057. All five dialects' DDL asserted via
  generator snapshots; live SQLite round-trip (insert computes, update recomputes). See
  [Schema DDL](../articles/features/schema-ddl.md).
- **Auditing user columns `[InquiryCreatedBy]`/`[InquiryModifiedBy]` (2026-06-13):** the who-changed-it
  counterpart to the timestamp auditing — a `string` column stamped from the ambient
  `InquiryAuditContext.CurrentUser` (an `AsyncLocal` set per request via `BeginScope`). `CreatedBy`
  is written once on insert when unset (null/empty) and excluded from every UPDATE SET across all
  five dialects (same machinery as `CreatedAt`); `ModifiedBy` advances on every insert/update/upsert
  including batch. Invalid type/placement is INQ055, duplicates INQ056. Live SQLite + generator
  snapshots + ambient-context unit tests. See [Auditing](../articles/features/auditing.md).
- **Derived query methods (2026-06-13):** a field-less `[InquirySelectAllByField]` infers its filter
  columns from the method name (Spring Data convention, compile-time): the segment after the first
  PascalCase `By`, split on `And` word boundaries, names the fields (`SelectByCountryAndCityAsync` →
  `Country` AND `City`; a trailing `Async` is ignored). Derived fields resolve through the normal
  column-resolution path (unknown → INQ007); an explicit field list still wins, and a name with no
  `By<Field>` segment is INQ054. Generator snapshots + live SQLite. See the derived-query section in
  [CRUD](../articles/features/crud.md).
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
  are atomic — SQL Server uses `UPDATE … IF @@ROWCOUNT = 0 INSERT` with `UPDLOCK, SERIALIZABLE` (client
  and generated key; upgraded from MERGE in #59), PostgreSQL uses `INSERT … ON CONFLICT` — so concurrent
  same-key upserts no longer throw a spurious duplicate-key error;
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
