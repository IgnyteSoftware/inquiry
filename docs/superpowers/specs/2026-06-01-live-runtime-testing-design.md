# Live-runtime testing & benchmarking for Inquiry providers

- **Date:** 2026-06-01
- **Status:** Approved (design) — pending spec review
- **Author:** jake.overstreet@ignyte.com (with Claude)

## 1. Problem & goals

Inquiry bakes provider-specific SQL into generated stores at compile time across five dialects
(SQLite, SQL Server, PostgreSQL, MySQL, Oracle). Today there is **no automated verification that the
generated SQL actually runs against the real engines**:

- Each provider has an integration test project with a `<Provider>TestHarness` and a
  `<Provider>FactAttribute`, but the facts are gated behind an `INQUIRY_<PROVIDER>_CONNECTION_STRING`
  env var that is never set, so they always skip.
- Worse, every provider test project references the shared `Inquiry.Northwind` assembly, which bakes
  its store SQL against the **SQLite dialect** (`[assembly: InquiryDialect("Sqlite")]`). So even if a
  database were provided, SQL Server / Postgres would run SQLite-dialect SQL (it "passes" by accident
  on those two), and MySQL / Oracle would fail outright. The provider's *own* emitted SQL is never
  exercised end-to-end.
- There is no CI, and the benchmark project can only target in-process SQLite.

Inquiry is infrastructure that, if wrong, can cause production downtime for its consumers. We must be
able to prove — on every PR — that each provider's generated SQL works against the real engine, over a
**faithful, complete** schema (keys, foreign keys, indexes, constraints), with **nothing silently
missed**.

### Goals

1. On every PR into `main`, run live integration tests against **PostgreSQL, MySQL, and SQL Server**
   (plus the existing SQLite + generator + unit suites). Run **Oracle** on a nightly/manual workflow.
2. A developer needs **only Docker** installed — no database engine on the host.
3. Each provider's tests exercise **its own dialect's** generated SQL against the real engine.
4. The schema created in each container is a **faithful, complete** Northwind: all tables, primary
   keys (including composite), foreign keys, the full classic secondary-index set, and all
   NOT NULL / DEFAULT constraints.
5. A **fidelity guardrail** proves, by catalog introspection, that the schema actually materialized —
   so an omission or drift fails the build loudly.
6. Separately verify that **Inquiry's own generated DDL** (`InquiryGeneratedSchema.Ddl`, from W7/W7b)
   stands up a working schema against each live engine and that CRUD passes against it — because that
   is the code path real consumers use to create their schema.
7. The same provisioning powers **live-environment benchmarking** sessions.

## 2. Non-goals

- Refactoring the multi-provider `Inquiry.Sample` app (pre-existing, separate concern).
- A polished cross-provider benchmark *suite*. We deliver the live-provisioning capability and one
  proof (PostgreSQL); additional providers/scenarios are incremental and follow the same pattern.
- Keeping the `INQUIRY_<PROVIDER>_CONNECTION_STRING` env-var path. Provisioning is pure
  Testcontainers. (An env-var override is a trivial future addition; noted in open questions.)
- Byte-for-byte equivalence between the hand-written Northwind DDL and Inquiry's generated DDL. The
  generated-DDL verification proves the generated schema *works* and contains the declared structure,
  not that it is textually identical.

## 3. Key decisions

| Decision | Choice |
|---|---|
| CI scope per PR | Postgres + MySQL + SQL Server + (SQLite/generator/unit); **Oracle nightly/manual** |
| Provisioning | **Testcontainers, in-process**, one container per test assembly (collection fixture) |
| Docker unavailable | **Skip gracefully** (`[SkippableFact]` + fixture availability flag) |
| Per-provider SQL | **Approach A** — one shared Northwind source set, linked-compiled into each provider test project with its own `[assembly: InquiryDialect]` + analyzer |
| Schema safety | **Faithful DDL (incl. full classic index set) + catalog fidelity guardrail + verify Inquiry's generated DDL against live engines** |
| Benchmarks | Reuse Testcontainers; dialect chosen by an MSBuild property; ship SQLite default + PostgreSQL proof |
| CI TFM coverage | Integration matrix on a single representative TFM (`net8.0`); non-DB suites keep full `net6.0`–`net10.0` span |

## 4. Architecture overview

Seven workstreams:

- **WS1 — Per-provider Northwind compilation.** Shared source, linked-compiled per dialect.
- **WS2 — Faithful schema.** Add the full classic secondary-index set to all five dialect DDLs;
  annotate the Northwind entities with the matching DDL metadata so Inquiry's generated DDL is also
  faithful.
- **WS3 — Testcontainers provisioning.** Per-assembly collection fixture; graceful skip.
- **WS4 — Schema-fidelity guardrail.** One declarative expected-schema model + per-provider catalog
  introspectors that assert the live schema matches.
- **WS5 — Generated-DDL verification.** Stand up a schema from `InquiryGeneratedSchema.Ddl` against
  each live engine; run CRUD + the fidelity guardrail against it.
- **WS6 — CI workflows.** PR matrix (PG/MySQL/SQL Server) + non-DB job; nightly Oracle.
- **WS7 — Live-environment benchmarking.** Dialect-parameterized benchmark that provisions via
  Testcontainers.

## 5. Detailed design

### WS1 — Per-provider Northwind compilation

The Northwind source (`samples/Inquiry.Northwind/Models/**`, `Stores/**`, `NorthwindSchema.cs`)
remains the single source of truth. Each non-SQLite provider test project:

1. **Drops** its `ProjectReference` to `Inquiry.Northwind` (the SQLite-baked assembly).
2. **Linked-compiles** the source via globbed `<Compile Include="..\..\samples\Inquiry.Northwind\Models\**\*.cs" />`
   (plus `Stores\**\*.cs` and `NorthwindSchema.cs`), **excluding** `AssemblyInfo.cs`.
3. Supplies its own dialect attribute in a project-local file, e.g.
   `[assembly: InquiryDialect("SqlServer")]`.
4. Keeps its existing `ProjectReference` to the provider package (e.g. `Inquiry.SqlServer`), which
   loads the matching analyzer.

Because the types stay in the `Inquiry.Northwind` namespace (now compiled in-assembly),
`using Inquiry.Northwind;` and `NorthwindSchema.SqlServerDdl` continue to resolve. The Sqlite test
suite and the sample/benchmark apps are untouched.

**Validation property:** if a store method cannot be emitted for a dialect, the test project fails to
**compile** — surfacing a real provider gap immediately, which is the desired behavior.

### WS2 — Faithful schema (indexes + constraints)

The current DDL has **zero secondary indexes** in all five dialects. Add the classic Northwind
non-clustered index set. Index naming uses `IX_<Table>_<Column>` (portable; avoids Oracle's global
index-namespace and identifier-length pitfalls). The fidelity guardrail (WS4) asserts indexes by
*table + column set*, not by name, to stay portable.

Classic Northwind secondary indexes to add (per the original `instnwnd.sql`):

| Table | Indexed column(s) |
|---|---|
| Categories | CategoryName |
| Customers | City; CompanyName; PostalCode; Region |
| Employees | LastName; PostalCode |
| Orders | CustomerID; EmployeeID; OrderDate; ShippedDate; ShipVia; ShipPostalCode |
| Products | CategoryID; ProductName; SupplierID |
| Suppliers | CompanyName; PostalCode |
| Order Details | OrderID; ProductID |

Notes:
- `Order Details(OrderID)` / `(ProductID)` and the FK leading-column indexes overlap PK-covered
  columns on some engines; they are included for faithfulness. The guardrail treats a PK/leading-column
  index as satisfying the expectation where the engine folds them.
- Confirm **foreign-key enforcement is actually on** per engine: Postgres/SQL Server/Oracle enforce by
  default; MySQL must use the **InnoDB** engine (modern default — assert it). SQLite (in-process) needs
  `PRAGMA foreign_keys=ON`, already handled by the existing harness `foreignKeys` flag.

To make Inquiry's **generated** DDL faithful too (for WS5), annotate the Northwind entities with the
DDL metadata that the W7/W7b emitter consumes:
- `[InquiryColumn(Length = n)]` on bounded string columns (matching the per-dialect VARCHAR lengths).
- `[InquiryColumn(IsIndexed = true)]` on the classic-index columns above.
- `[InquiryColumn(Precision/Scale)]` on decimal columns (e.g. `UnitPrice`, `Freight`: 19,4).

This converges the two schema sources: the hand-written per-dialect DDL stays the primary
schema-under-test, and the generated DDL (WS5) produces the same declared keys/FKs/indexes.

### WS3 — Testcontainers provisioning + graceful skip

Add the per-provider Testcontainers modules. Each provider test project gains an xUnit **collection
fixture** implementing `IAsyncLifetime`:

- `InitializeAsync`: start **one** container for the whole assembly; on success expose the admin
  connection string and set `IsAvailable = true`. On any Docker/connection failure, catch it, store
  the reason, and set `IsAvailable = false` (never throw — that would fail discovery).
- `DisposeAsync`: stop/remove the container.

The existing `<Provider>TestHarness.CreateAsync` is refactored to take the admin connection string
**from the fixture** (instead of reading the env var) and otherwise keeps its current behavior: create
a throwaway database/schema, run the (now faithful) Northwind DDL, build the DI container, and drop the
database/schema on disposal. One container per assembly; one throwaway database per test.

Graceful skip: replace the env-var-based `<Provider>FactAttribute` with `[SkippableFact]`
(`Xunit.SkippableFact`). Each integration test (or a shared base) calls
`Skip.IfNot(Fixture.IsAvailable, Fixture.SkipReason)` at the top. A developer without Docker sees
skips and a green run for the non-DB suites; CI requires Docker and therefore enforces them.

Container images (pinned via the Testcontainers modules): `postgres`, `mysql`, `mcr.microsoft.com/mssql/server`,
and `gvenzl/oracle-free` for Oracle.

### WS4 — Schema-fidelity guardrail

One declarative **expected-schema model** describes Northwind once: tables → columns (with
nullability), primary keys, foreign keys (table+columns → referenced table+columns), and the secondary
indexes from WS2. Provider-agnostic.

A per-provider **introspector** reads the live catalog after the schema is created and produces the
same model shape, which is compared against the expectation; any missing or extra table, column,
nullability mismatch, PK, FK, or index **fails** the test with a precise message. Catalog sources:

| Engine | Catalog views |
|---|---|
| PostgreSQL | `information_schema.tables/columns`, `pg_indexes`, `information_schema.table_constraints` + `key_column_usage`, `pg_constraint` (FKs) |
| SQL Server | `sys.tables`, `sys.columns`, `sys.indexes`/`sys.index_columns`, `sys.key_constraints`, `sys.foreign_keys`/`sys.foreign_key_columns` |
| MySQL | `information_schema.TABLES/COLUMNS/STATISTICS/TABLE_CONSTRAINTS/KEY_COLUMN_USAGE` |
| Oracle | `user_tables`, `user_tab_columns`, `user_constraints`/`user_cons_columns`, `user_indexes`/`user_ind_columns` |
| SQLite | `PRAGMA table_info`, `PRAGMA foreign_key_list`, `PRAGMA index_list`/`index_info` (added for parity) |

The expectation matches on structure (table + column set), not on engine-specific physical type names
or index names, so it stays portable while still guaranteeing completeness.

### WS5 — Verify Inquiry's generated DDL against live engines

In each provider test project the Northwind entities are compiled with that dialect, so
`InquiryGeneratedSchema.Ddl` (internal, from W7/W7b) is available. A dedicated test:

1. Creates a fresh throwaway database/schema.
2. Executes `InquiryGeneratedSchema.Ddl` (splitting statements where the engine has no multi-statement
   batch, e.g. Oracle).
3. Runs the **CRUD suite** against it (insert/select/update/delete/upsert across the Northwind stores).
4. Runs the **fidelity guardrail** (WS4) to assert the entity-declared keys/FKs/indexes (from the WS2
   annotations) materialized.

This proves the consumer-facing DDL path produces a working, structurally-complete schema on the real
engine — the most direct guard against "Inquiry caused the outage."

### WS6 — CI workflows

`.github/workflows/ci.yml` (triggers: `pull_request` to `main`, `push` to `main`):
- **Job `build-and-unit`** (`ubuntu-latest`): restore/build the solution; run the generator, unit, and
  SQLite suites across `net6.0`–`net10.0` (`actions/setup-dotnet` installing the needed SDKs).
- **Job `integration`** (`ubuntu-latest`, `strategy.matrix.provider: [postgres, mysql, sqlserver]`):
  run `dotnet test` for that provider's test project on `net8.0`. GitHub-hosted Linux runners ship
  Docker, so Testcontainers works with no extra setup.

`.github/workflows/nightly.yml` (triggers: `schedule` cron + `workflow_dispatch`):
- Run the **Oracle** integration project (`gvenzl/oracle-free`). Optionally re-run the full matrix.

Windows developers use Docker Desktop; CI uses Linux Docker — the same Testcontainers code path.

### WS7 — Live-environment benchmarking

A small shared provisioning helper (Docker-availability probe + "start container → connection string"
wrapper) is consumed by both the fixtures and the benchmark. The benchmark project gains an MSBuild
property `InquiryBenchProvider` (default `Sqlite`):

- Default `Sqlite`: today's in-process micro-benchmark (Inquiry vs Dapper vs EF), unchanged.
- `PostgreSql` (and later others): conditionally include the matching `[assembly: InquiryDialect]`
  file, reference that provider package + Testcontainers module, and linked-compile the Northwind
  source instead of referencing the SQLite assembly.

`dotnet run -c Release -p:InquiryBenchProvider=PostgreSql` then benchmarks against a Dockerized
Postgres (container started once per session, outside the measured region). v1 ships the `Sqlite`
default + the PostgreSQL proof; other engines follow the identical pattern.

## 6. Package changes (`Directory.Packages.props`)

Add (pinned versions): `Testcontainers.PostgreSql`, `Testcontainers.MySql`, `Testcontainers.MsSql`,
`Testcontainers.Oracle`, and `Xunit.SkippableFact`. Each test project references **only** its own
Testcontainers module to keep dependency surface minimal.

## 7. Rollout / sequencing

1. **WS2 schema work first** (faithful DDL + entity annotations) — independent and reviewable on its
   own; everything downstream depends on it.
2. **PostgreSQL end-to-end** as the template: WS1 compile change + WS3 fixture + WS4 guardrail + WS5
   generated-DDL test, green locally with Docker, plus the CI matrix leg (WS6). Lowest-friction engine.
3. Fan out **MySQL** and **SQL Server** following the template.
4. **Oracle** last (nightly), absorbing the `BindByName` / `:`-bind questions the harness docstrings
   flag — the live run resolves them empirically.
5. **WS7** benchmark parameterization + PostgreSQL proof.

## 8. Risks & open questions

- **Northwind portability under every dialect.** A store shape that one analyzer can't emit becomes a
  compile error (WS1). Mitigation: the existing generator emission tests already cover these shapes;
  any gap is a real bug we want surfaced.
- **Oracle container weight.** `gvenzl/oracle-free` is multi-GB with a slow cold start; confined to
  nightly to keep PR CI fast.
- **`net10.0` SDK on runners.** If not yet GA on hosted runners, pin via `setup-dotnet` quality/version
  or drop net10 from the CI matrix leg (full TFM span still builds locally).
- **Redundant-index warnings** on PK-overlapping indexes (e.g. `Order Details`): accepted for
  faithfulness; the guardrail tolerates engine folding.
- **Deferred:** an optional `INQUIRY_<PROVIDER>_CONNECTION_STRING` override (to target a warm container
  or a cloud instance) is a trivial future addition; not built now per the chosen pure-Testcontainers
  model.

## 9. Success criteria

- `dotnet test` on a dev box **with Docker** runs each provider's Northwind CRUD suite green against a
  real engine using that provider's own generated SQL; **without Docker**, those tests skip and the
  rest stay green.
- The fidelity guardrail fails if any expected table/column/PK/FK/index is missing from the live
  schema — verified by a deliberately-broken-schema spot check during implementation.
- A schema stood up purely from `InquiryGeneratedSchema.Ddl` passes CRUD + the guardrail on every
  engine.
- A PR into `main` runs Postgres + MySQL + SQL Server integration green; the nightly workflow runs
  Oracle green.
- `dotnet run -c Release -p:InquiryBenchProvider=PostgreSql` benchmarks against a Dockerized Postgres.
