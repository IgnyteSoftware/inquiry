# Inquiry — Project Status & Onboarding

> **Start here.** This is the single source of truth for *where the project is*, *how we develop it*,
> and *what's left to do*. For the architecture deep-dive (compile-time SQL generation, the runtime
> pipeline) read [`../README.md`](../README.md). For behavioral coding guidelines read
> [`../CLAUDE.md`](../CLAUDE.md). For the design/dependency record of past work see [`plans/README.md`](plans/README.md).

- **Last reconciled:** 2026-06-01, at commit `fcfaa3e` (`main`).
- **One-line:** Inquiry is a compile-time-SQL micro-ORM — a Roslyn incremental source generator that
  bakes every SQL statement as a `const string` at build time. The runtime ships zero SQL.

---

## 1. Where it's at

### Supported database engines (5, all live-tested)

| Dialect (`[assembly: InquiryDialect("…")]`) | Runtime package | Analyzer (source generator) | Live test status |
|---|---|---|---|
| `Sqlite` | `src/Inquiry.Sqlite` | `src/Inquiry.Sqlite.Analyzer` | in-process (no Docker) |
| `SqlServer` | `src/Inquiry.SqlServer` | `src/Inquiry.SqlServer.Analyzer` | Testcontainers (PR CI) |
| `PostgreSql` | `src/Inquiry.PostgreSql` | `src/Inquiry.PostgreSql.Analyzer` | Testcontainers (PR CI) |
| `MySql` | `src/Inquiry.MySql` | `src/Inquiry.MySql.Analyzer` | Testcontainers (PR CI) |
| `Oracle` | `src/Inquiry.Oracle` | `src/Inquiry.Oracle.Analyzer` | Testcontainers (nightly) |

The shared generator framework lives in `src/Inquiry.Generators.Shared` and is bundled privately into
each `*.Analyzer` (Roslyn loads each analyzer in its own `AssemblyLoadContext`, so the framework cannot
be a shared analyzer dependency).

### Feature completeness — the 13-workstream roadmap is **DONE**

Every workstream in [`plans/README.md`](plans/README.md) is implemented and merged to `main`:

| ID | Workstream | ID | Workstream |
|----|-----------|----|-----------|
| E1 | MySQL / MariaDB provider ✅ | W5 | Projections + aggregations ✅ |
| E2 | Oracle provider ✅ | W6 | Optimistic concurrency / row-versioning ✅ |
| E3 | Cloud-compat (Azure SQL / CockroachDB / Aurora retry) ✅ | W7 | Migrations / schema DDL (Phase A) ✅ |
| W1 | Richer WHERE predicates ✅ | W8 | Soft deletes ✅ |
| W2 | ORDER BY + pagination (offset + keyset) ✅ | W9 | Full-text search ✅ |
| W3 | Batch & bulk operations ✅ | W10 | JSON / array / value-converter columns ✅ |
| W4 | Automatic prepared-statement reuse ✅ | | |

### Live-runtime testing — Phases 0–7 **DONE**, Phase 8 deferred

The [live-runtime testing plan](superpowers/plans/2026-06-01-live-runtime-testing.md) delivered:
per-dialect compilation of the shared Northwind source, one Testcontainers container per provider test
assembly (graceful skip when Docker is absent), a faithful fully-indexed Northwind schema, a
catalog-introspection **fidelity guardrail** ([`tests/Inquiry.IntegrationTesting`](../tests/Inquiry.IntegrationTesting)),
verification of Inquiry's *own* generated DDL (`InquiryGeneratedSchema.Ddl`) against each live engine,
and CI. Only the **live-environment benchmark (Phase 8/9)** remains — intentionally deferred (see §3).

### Test status (snapshot as of `fcfaa3e`, net8.0)

| Suite | Tests | Needs Docker? |
|---|---|---|
| `Inquiry.Generators.Tests` (emission + per-dialect SQL) | 160 | no |
| `Inquiry.Tests` (runtime pipeline, binding, transactions) | 92 | no |
| `Inquiry.Sqlite.Tests` (in-process e2e + fidelity) | 104 | no |
| `Inquiry.PostgreSql.Tests` | 38 | yes |
| `Inquiry.SqlServer.Tests` | 36 | yes |
| `Inquiry.MySql.Tests` | 36 (+1 documented skip) | yes |
| `Inquiry.Oracle.Tests` | 27 (no skips) | yes |

All green. Docker-gated suites **skip** (not fail) when Docker is unavailable. Regenerate counts with
`dotnet test` (whole solution) or per project, e.g. `dotnet test tests/Inquiry.MySql.Tests -f net8.0`.

---

## 2. How we develop (process)

This project is built with the **superpowers** skill workflow and a few hard conventions:

- **Skill-first.** Start work through the relevant skill: `superpowers:brainstorming` for a new
  feature → write a spec → `superpowers:writing-plans` → execute with
  `superpowers:subagent-driven-development` / `executing-plans`. Use `superpowers:debugging` for bugs.
- **Worktrees + parallel agents.** Large, separable workstreams are built in isolated git worktrees by
  parallel agents, then merged one at a time. The "hot spine" of shared generator files (see
  [`plans/README.md`](plans/README.md)) is edited via a serialized **foundation** pass first, so parallel
  branches don't collide. For small, short-lived tasks, work in-session rather than spawning agents.
- **TDD.** Red generator-emission test first (assert the exact emitted `const string`) → implement →
  integration test (SQLite always-on; the other dialects via Testcontainers).
- **Live testing needs only Docker.** No database engine is installed on the host. Each provider test
  project linked-compiles the Northwind source under *its own* dialect so it exercises that engine's
  real SQL. Containers come from Testcontainers; tests skip gracefully without Docker.
- **Code review before merge.** Run the superpowers code-review skill on a feature branch before
  merging. Fix Critical/Important findings first.
- **Merge to `main` directly — NO pull requests.** Merge a feature branch into `main` once it's
  complete, reviewed, and green.
- **Commit messages** are written to a BOM-free file and committed via `git commit -F`, ending with the
  `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>` trailer. (PowerShell
  here-strings bind unreliably to native git; the file approach is the convention.)
- **CI.** [`.github/workflows/ci.yml`](../.github/workflows/ci.yml) runs the PR matrix — PostgreSQL,
  MySQL, SQL Server live suites + the non-Docker unit/generator/SQLite suites.
  [`.github/workflows/nightly.yml`](../.github/workflows/nightly.yml) runs Oracle on a cron.
- **Adding a provider** follows the append-point checklist in [`plans/adding-a-provider.md`](plans/adding-a-provider.md).

---

## 3. What's upcoming (remaining work)

Nothing blocks `main`; everything below is follow-up. Tracked items use the in-session task list
(`TaskList`). Ordered by value.

### A. Schema-fidelity / generated-DDL (highest value — tied to "never cause production downtime")
1. **✅ Resolved (this session) — Oracle generated DDL now fully stands up; test un-skipped.**
   Quoting: `"Order Details"` (embedded space) was emitted unquoted → ORA-00903; `OracleSqlBuilder.QuoteIdentifier`
   now double-quotes identifiers that aren't legal unquoted (single chokepoint → DDL + DML in lockstep; test
   `OracleSchemaQuotesOnlyIdentifiersThatRequireIt`). The index-on-CLOB blocker that surfaced next (**ORA-02327**)
   was fixed under **item #2**. `Oracle.Tests/GeneratedDdlIntegrationTests` is now un-skipped and green (**Oracle 25, no skips**).
2. **✅ Resolved (this session) — unannotated string lengths on bounded-key dialects.**
   (a) A string FK with no `Length` now inherits its referenced PK's `Length` (`SchemaEmitter.DeriveForeignKeyLength`),
   so a bounded dialect emits a valid bounded `VARCHAR` instead of an unindexable/unkeyable LOB.
   (b) New **INQ032** (Warning) when a bounded dialect skips an index on an unbounded string, so the dropped
   index is not silent. (c) The index-skip itself was hoisted into the base `BuildCreateIndexSql` (gated by
   `RequiresBoundedStringKeys`) — giving Oracle the behavior MySQL/SQL Server already had (fixing ORA-02327)
   **and resolving the #10 dedup**. Northwind's ~11 indexed strings were annotated with `Length` (matching the
   hand-written DDL), so the sample's indexes are real and emit no warnings. Tests:
   `ForeignKeyStringColumnInheritsReferencedKeyLength`, `IndexedUnboundedStringReportsInq032OnBoundedDialectOnly`,
   `OracleSchemaSkipsIndexOnUnboundedStringButKeepsBoundedOne`.
3. **✅ Resolved (this session) — generated DDL now emits FKs on composite-key bridge tables.**
   `CustomerCustomerDemo`, `EmployeeTerritories`, and `Order Details` modeled their FK columns as
   `[InquiryKey]` only, so the generated DDL had no `FOREIGN KEY` for them. The generator already reads
   key/FK independently, so the 6 composite-key columns now also carry `[InquiryForeignKey]` (a column can
   be both). `SchemaFidelity.AssertStructure` was tightened from tables+PK to **tables+PK+FKs** (sharing a
   `CheckForeignKeys` helper with `AssertMatches`); the generated-DDL suites now verify the full FK set
   round-trips on every live dialect. Generator test `CompositeKeyColumnsCanAlsoBeForeignKeys`. (Columns
   and secondary indexes stay model-bounded, so `AssertStructure` still skips those.)

### B. Live test coverage — ADDED 2026-06-01
4. **MySQL & Oracle live coverage added** — table-breadth + pagination + predicate suites
   (`NorthwindCoverageIntegrationTests` / `PaginationIntegrationTests` / `PredicateSelectIntegrationTests`).
   MySQL is fully green (37 tests). Oracle's pagination/predicate paths are broken (see #6), so those
   facts are documented as ready-to-un-skip tests; the returning-heavy table-breadth suite is N/A on
   Oracle (#8). **This coverage fixed the Oracle bool-binding bug and confirmed #5 and #6.**

   **✅ Resolved this session — Oracle boolean binding.** Inserting any entity with a `bool` column
   failed with ORA-00932 (Oracle has no BOOLEAN type; `bool` maps to `NUMBER(1)`, and ODP.NET would not
   coerce a CLR bool). Fixed in `OracleInquiryConnectionFactory.FinalizeCommand` (bool → 0/1 +
   `DbType.Int32`); regression test `Oracle.Tests/NorthwindCrudIntegrationTests.BooleanColumnRoundTripsThroughNumber`.

### C. Provider runtime limitations surfaced by live testing
5. **MySQL upsert-returning `LAST_INSERT_ID()`** — **CONFIRMED real** by live test: generated-key
   `UpsertReturningAsync` over the `ON DUPLICATE KEY UPDATE` branch returns `null` (LAST_INSERT_ID() is
   not set on the update path). Fix in `MySqlSqlBuilder.BuildUpsertReturningSql` (the `LAST_INSERT_ID(id)`
   trick or a key predicate). Ready-to-un-skip regression test:
   `MySql.Tests/NorthwindCoverageIntegrationTests.ProductUpsertReturningUpdateBranchIsKnownLimitation`.
6. **✅ Resolved this session — Oracle `@` parameter sigil.** Predicate (W1) and pagination (W2)
   parameters were baked into the const SQL with `@`, which Oracle rejected (ORA-00936). Fixed by making
   the prefix dialect-aware in the shared generator: `SqlPredicate` now carries the bare logical name and
   `SqlBuilder.RenderPredicate` applies the sigil; `BuildSelectPlanSql` builds synthetic paging params via
   `SqlBuilder.ParameterName`. Oracle bind names also cannot begin with `_`, so `OracleSqlBuilder.ParameterName`
   and `FinalizeCommand` both trim the leading underscores of the synthetic names. **Verified live:**
   `Oracle.Tests/PaginationIntegrationTests` (8) + comparison/LIKE/BETWEEN/OR/IS-NULL predicates pass. Non-Oracle
   emission is byte-identical; full suites green (Generators 153, SQLite 104, MySQL 36).

   **✅ Resolved this session — Oracle `IN` predicate.** The runtime IN-expansion (`InquiryInExpansion`)
   rewrites the command text by locating the baked sentinel, so the `Expand` call must pass the *dialect*
   sigil form (the scalar `@`/`:` reconciliation in `FinalizeCommand` can't bridge a text mismatch).
   `StoreProcessor.TryResolvePredicates` now builds the IN binding via `SqlBuilder.ParameterName` (`:name`
   on Oracle, `@name` elsewhere); the per-element expansion params are reconciled by `FinalizeCommand`
   under `BindByName`. **Verified live:** the 3 `Oracle.Tests/PredicateSelectIntegrationTests` IN cases
   pass; new generator regression test `OracleDialectEmitsInSentinelWithColonParameterAndExpansion`.
7. **✅ Resolved (this session) — Oracle `@keys` batch-delete sentinel.** `BuildDeleteAllByKeysSql`/
   `BuildSoftDeleteAllByKeysSql` baked `IN (@keys)` and the emitter passed `@keys` to `InquiryInExpansion`,
   which Oracle rejected (same root as the predicate `IN`). Both now take the dialect sigil via
   `SqlBuilder.ParameterName("keys")` (`:keys` on Oracle, `@keys` elsewhere); the emitter receives the
   `SqlBuilder` and passes the matching name. (`UpdateAll` was never affected — it uses per-row `@u{r}_n`
   params, not `@keys`.) Verified live by `Oracle.Tests/BatchDeleteIntegrationTests` (2) + generator test
   `OracleDeleteAllUsesColonKeysSentinelAndExpansion`.
8. **Oracle RETURNING / `ReturnEntity=true`** — unsupported via the reader pipeline; currently degrades
   to an `INQ039` throwing stub (graceful, but a functional limitation to document or solve).

### D. Deferred plan item & cleanup
9. **Live-environment benchmark (Phase 8/9 of the live-runtime plan)** — dialect-parameterize
   `benchmarks/Inquiry.Benchmarks` (`InquiryBenchProvider` MSBuild property + `AssemblyDialect.*.cs`)
   and add a PostgreSQL benchmark provisioning via Testcontainers. Infra exists; explicitly deferred.
10. **✅ Resolved (this session) — dedup `BuildCreateIndexSql` / `IsUnboundedString`.** The identical SQL
    Server and MySQL index-skip overrides were hoisted into the base `SqlBuilder` (gated by
    `RequiresBoundedStringKeys`); both per-dialect copies are gone, and Oracle now shares the behavior. Done
    as part of item #2.

---

## 4. Doc map

| Doc | What it is |
|---|---|
| [`STATUS.md`](STATUS.md) (this file) | Current state · process · upcoming. Start here. |
| [`../README.md`](../README.md) | Architecture deep-dive: source-generation, SQL building, runtime pipeline. |
| [`../CLAUDE.md`](../CLAUDE.md) / [`../AGENTS.md`](../AGENTS.md) | Behavioral coding guidelines for agents. |
| [`plans/README.md`](plans/README.md) | The 13-workstream roadmap — dependency graph, hot-spine map, wave order. Design record (all done). |
| [`plans/*.md`](plans) | One self-contained design spec per workstream (engine-*, feature-*). |
| [`plans/adding-a-provider.md`](plans/adding-a-provider.md) | Append-point checklist for a new dialect. |
| [`superpowers/specs/2026-06-01-live-runtime-testing-design.md`](superpowers/specs/2026-06-01-live-runtime-testing-design.md) | Approved design for live-runtime testing. |
| [`superpowers/plans/2026-06-01-live-runtime-testing.md`](superpowers/plans/2026-06-01-live-runtime-testing.md) | Task-by-task plan (Phases 0–7 done; Phase 8 deferred). |
