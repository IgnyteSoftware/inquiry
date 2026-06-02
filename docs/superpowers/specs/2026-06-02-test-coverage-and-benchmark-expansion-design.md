# Design — Expand Test Coverage & Benchmarks

- **Date:** 2026-06-02
- **Status:** Approved (scope confirmed via scoping questions: full parity, benchmark `[Params(1000, 100000)]`, verify all four live containers incl. Oracle).
- **Owner:** in-session work, project process (brainstorm → spec → plan → execute).

## 1. Goal

Expand test coverage across **all** suites to cover the full range of supported features, and expand the
benchmarks to a larger data set covering all important features Inquiry has added.

Two concrete outcomes:

1. **Uniform live feature matrix** — every supported feature is exercised live on every dialect that
   supports it (PostgreSQL, SQL Server, MySQL, Oracle), not just on in-process SQLite.
2. **Benchmarks at scale + breadth** — read benchmarks run at `[Params(1000, 100000)]` rows, and the
   important added features (pagination, batch, projections/aggregations, predicates, eager loading,
   composite keys) gain benchmarks alongside the existing CRUD set.

## 2. Current state (gap analysis)

~440 tests today. **Non-Docker suites are strong** (Generators 136, Runtime 75, SQLite e2e 107 — cover
every workstream). **Docker-gated live suites are uneven.** Live parity matrix *before* this work:

| Live feature | PostgreSQL | SQL Server | MySQL | Oracle |
|---|---|---|---|---|
| CRUD / RETURNING / upsert / composite keys / eager / transactions | ✅ | ✅ | ✅ | ✅ |
| W1 predicates (LIKE/BETWEEN/IN/OR/IS NULL) | ❌ | ❌ | ✅ | ✅ |
| W2 pagination (offset + keyset) | ❌ | ❌ | ✅ | ✅ |
| W3 batch insert/update/delete | ❌ | ❌ | ❌ | delete only |
| W5 projections / aggregations | ❌ | ❌ | ❌ | ❌ |
| W6 optimistic concurrency | ❌ | ❌ | ❌ | ❌ |
| W8 soft deletes | ❌ | ❌ | ❌ | ❌ |
| W9 full-text search (live execution) | ❌ | ❌ | ❌ | ❌ (never run live anywhere) |
| W10 JSON / value-converter columns | ❌ | ❌ | ❌ | ❌ |

Benchmarks: fixed **1000 rows** (small, non-parameterized); only basic CRUD (SelectAll/ByKey/ByField/
Insert/Update/Upsert) on three entities. No pagination/batch/projection/aggregation/eager/predicate/
composite-key/concurrency/JSON benchmarks.

## 3. Architecture & key decisions

### 3.1 How the live suites share code
Each dialect test project **glob-links** the shared Northwind source and compiles it under its own
analyzer:
```xml
<Compile Include="..\..\samples\Inquiry.Northwind\Models\**\*.cs"  LinkBase="Northwind\Models" />
<Compile Include="..\..\samples\Inquiry.Northwind\Stores\**\*.cs"  LinkBase="Northwind\Stores" />
<Compile Include="..\..\samples\Inquiry.Northwind\NorthwindSchema.cs" LinkBase="Northwind" />
```
So **new entities/stores added under `Models/`–`Stores/` propagate to all suites automatically**; a new
`FeatureSchema.cs` needs one `<Compile>` line per suite.

### 3.2 Decision: shared "feature catalog", Northwind kept canonical
Northwind's classic schema has no soft-delete flag, concurrency token, JSON column, or full-text index.
Rather than mutate it (which would break the `ExpectedNorthwindSchema` fidelity guardrail), add a small
**feature catalog** of purpose-built entities with per-dialect DDL in a new `FeatureSchema` class.
Projections/aggregations need no new columns, so those go on existing Northwind stores.

*Alternative rejected:* adding feature columns to Northwind tables — breaks schema fidelity and the
faithful-Northwind property.

### 3.3 Decision: FTS source isolated from Sqlite-compiled projects
The Northwind sample compiles under **Sqlite** with `TreatWarningsAsErrors=true`, and SQLite FTS is
rejected with **INQ035**. Therefore W9 FTS entity/store source must **not** live in any Sqlite-compiled
project (the Northwind sample or the SQLite test project). It lives in a dedicated source location linked
**only** into the FTS-capable dialect test projects. SQLite keeps its existing INQ035 rejection emission
test.

### 3.4 Decision: per-dialect feature harness
Each live suite gains a small `FeatureTestHarness` (mirrors the existing `*TestHarness`) that stands up
`FeatureSchema.<Dialect>Ddl` instead of the Northwind DDL, via the existing `CreateFromDdlAsync` seam.
FTS needs a real full-text index, applied as part of the FTS DDL.

### 3.5 Verified feature APIs (from existing SQLite fixtures)
- **W5 aggregate:** `[InquiryCount]`, `[InquiryAggregate(InquiryAggregateFunction.Sum, "Col")]`, `Max`.
- **W5b projection:** `[InquiryProjection(typeof(Entity))]` on a `record` + projection-returning methods.
- **W3 batch:** `[InquiryInsertAll]`, `[InquiryDeleteAll]` (exists on `RegionStore`), batch update.
- **W6 concurrency:** `[InquiryConcurrencyToken]`; `AddInquiry(o => o.ThrowOnConcurrencyConflict = true)`;
  `InquiryConcurrencyException`.
- **W8 soft delete:** `[InquirySoftDelete]`, `[InquirySelectAll(IncludeDeleted = true)]`,
  `[InquiryDeleteOneByKey(HardDelete = true)]`, `[InquiryRestoreOneByKey]`, `[InquiryCount]`.
- **W10:** `[InquiryColumn("C", Converter = typeof(XConverter))]` + `IInquiryValueConverter<TModel,TProv>`;
  `[InquiryColumn("Tags"), InquiryJson]`.
- **W9:** `[InquiryFullTextSearch("Title","Body")]` → PG `to_tsvector … @@ plainto_tsquery`, SQL Server
  `FREETEXT(([Title],[Body]), @t)`, MySQL `MATCH(...) AGAINST (@t IN NATURAL LANGUAGE MODE)`.

## 4. Workstreams

### A. Live W1/W2/W3 parity (test-only where methods exist)
- **PostgreSQL + SQL Server:** add `PredicateSelectIntegrationTests` and `PaginationIntegrationTests`
  mirroring the existing MySQL/Oracle files (store methods already exist on `ProductStore`).
- **Batch (W3):** add `BatchDeleteIntegrationTests` to PostgreSQL/SQL Server/MySQL (via `RegionStore`);
  add `InsertAll`/`UpdateAll` batch methods where missing on a Northwind store and `BatchWriteIntegrationTests`
  across all four dialects (red emission test first for any new store method).
- **Oracle gap-fills:** multi-column WHERE predicate test + self-referential FK (`Employee.ReportsTo`)
  round-trip, to match the other three.

### B. W5 projections & aggregations (new store methods + tests)
- Add `[InquiryCount]`, `[InquiryAggregate(Sum/Max/Min/Avg…)]` and a projection record + projection
  methods to a Northwind store (e.g. `OrderDetailStore` for SUM(UnitPrice*Quantity) style, `ProductStore`
  for COUNT/MAX). **Red generator-emission tests for all five dialects first**, then live integration
  tests on all four engines (+ keep/extend the SQLite ones).

### C. Feature catalog — W6 / W8 / W10 live on all dialects
- New shared entities under the catalog: `VersionedItem` (W6 concurrency token), `SoftDeletableItem`
  (W8 soft-delete flag), `JsonDocument` (W10 JSON + value-converter columns), with stores.
- New `FeatureSchema` with `SqliteDdl` / `PostgreSqlDdl` / `SqlServerDdl` / `MySqlDdl` / `OracleDdl`
  constants (respect dialect types: SQL Server `rowversion`/`bit`/`nvarchar(max)`, PG `jsonb`, MySQL
  `json`, Oracle `NUMBER(1)`/`CLOB`).
- `FeatureTestHarness` per suite; live integration tests exercising concurrency conflict, soft-delete
  hide/restore/hard-delete/count, and JSON/converter round-trip on all four engines.
- Emission tests (non-Docker) for the new entities' per-dialect SQL.

### D. W9 full-text search — first live execution
- FTS entity/store (`Article` with `Title`/`Body` + `[InquiryFullTextSearch]`) isolated from
  Sqlite-compiled projects; per-dialect FTS DDL **including the full-text index**.
- Live tests on **PostgreSQL, MySQL** (definite). **SQL Server** gated on full-text component
  availability in the container (env-gated skip + clear `SkipReason` if absent). **Oracle:** verify
  whether `OracleSqlBuilder` implements FTS; if not, add an Oracle rejection/diagnostic emission test and
  document the gap in STATUS.md rather than fake it.

### E. Benchmarks — scale + breadth
- **Scale:** introduce `[Params(1000, 100000)]` (`SeedRows` becomes the param) on the read benchmarks;
  seed 100k via batched/multi-row INSERT in `[GlobalSetup]` (one-time), not per-iteration.
- **Breadth (in-process SQLite, vs Dapper/EF/ADO baseline):** new benchmark classes —
  `PaginationBenchmarks` (offset + keyset), `BatchBenchmarks` (InsertAll/DeleteAll vs per-row loops),
  `ProjectionBenchmarks` (projection vs full-entity + COUNT/SUM), `PredicateBenchmarks` (WHERE with
  parameters), `EagerLoadingBenchmarks` (separate-query parent+children), `CompositeKeyBenchmarks`.
- **Cross-dialect:** extend `CrossDialectReadBenchmarks` to the 100k tier for SelectAll/SelectByKey.
- Update `benchmarks/Inquiry.Benchmarks/README.md` to document the new params, classes, and seeding.

## 5. Testing & verification strategy

- **TDD per project convention:** every new store method (B, C, D) gets a **red generator-emission test
  asserting the exact emitted `const string` first**, then implementation, then the live integration test.
  Pure parity tests (A) are integration-only (no new SQL generated).
- **Green baseline first**, then re-run after each workstream.
- **Live verification:** run the non-Docker suites + **all four** Testcontainers suites locally including
  Oracle; live tests stay `[SkippableFact]` + `Skip.IfNot(fixture.IsAvailable, …)`.
- **Benchmarks:** smoke-test wiring with `--job short` at both param tiers (full runs are optional/manual).

## 6. Risks & mitigations

1. **SQLite/FTS compile constraint** → W9 source excluded from all Sqlite-compiled projects (§3.3).
2. **SQL Server container may lack full-text** → env-gated skip with explicit `SkipReason`; PG/MySQL carry
   the definite W9 live coverage.
3. **Oracle FTS may be unimplemented** → verify; document as a known gap + rejection test, don't fake.
4. **Schema fidelity** → feature catalog is *separate* tables; `ExpectedNorthwindSchema` untouched.
5. **100k seeding time** → batched insert in one-time `[GlobalSetup]`; keep 1000 tier for the
   library-overhead signal.
6. **Oracle dialect quirks** (`:` sigil, NUMBER(1) bool, RETURNING via PL/SQL, no batch DDL) → reuse the
   already-proven patterns in the existing Oracle suite.

## 7. Out of scope

- New ORM *features* (this is coverage work, not feature work). If a test surfaces a real bug, fix it
  narrowly and add a regression test (the project's TDD norm), but do not add new capabilities.
- Window functions, recursive CTEs, set operations, isolation-level/distributed-transaction tests — not
  Inquiry features.
- Full multi-TFM benchmark runs in CI (benchmarks remain a manual/Release tool).

## 8. Success criteria

- The live parity matrix in §2 is **all ✅** (or an explicit, documented gap for Oracle FTS).
- New non-Docker emission tests for every new store method are green.
- `dotnet test` green for the non-Docker suites; all four live suites green against local containers.
- Benchmarks compile and smoke-run at `[Params(1000, 100000)]`; the new benchmark classes run under
  `--job short`.
- STATUS.md test-status table updated to the new counts; benchmark README updated.
