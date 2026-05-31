# Inquiry Feature Roadmap — Parallel Implementation Plan

> Status: **planning only** (no code written). Generated 2026-05-31 from a deep-research pass on database-engine
> features + a 13-way parallel design study of the inquiry codebase.
>
> Goal: add the database engines and concepts inquiry is missing, structured so the work can be executed in
> **parallel git worktrees** with minimal merge conflict. You sequence the merges; this doc gives you the
> dependency graph, the shared-file "hot spine", a recommended wave order, and one self-contained spec per
> workstream.

## What inquiry is (context for every spec)

Compile-time-SQL micro-ORM = Roslyn incremental source generator. You declare entities + `partial` store methods;
the generator bakes every SQL statement as a `const string` and emits zero-allocation struct materializers. Runtime
ships no SQL and no dialect type. Today targets **SQLite, SQL Server, PostgreSQL**; supports CRUD (+RETURNING),
upsert, composite/generated keys, FK metadata, eager loading via **separate queries (no JOINs)**, transactions,
equality-only WHERE, stored-proc passthrough.

## The 13 workstreams

| ID | Workstream | Size | Spec |
|----|-----------|------|------|
| **E1** | MySQL / MariaDB provider | M | [spec](engine-mysql.md) |
| **E2** | Oracle provider | L | [spec](engine-oracle.md) |
| **E3** | Cloud-compat modes (Azure SQL / CockroachDB / Aurora) | S–M | [spec](engine-cloud-compat.md) |
| **W1** | Richer WHERE predicates | L | [spec](feature-where-predicates.md) |
| **W2** | ORDER BY + pagination (offset + keyset) | L | [spec](feature-pagination.md) |
| **W3** | Batch & bulk operations | L | [spec](feature-batch-bulk.md) |
| **W4** | Automatic prepared-statement reuse | M | [spec](feature-prepared-statements.md) |
| **W5** | Projections + aggregations | L | [spec](feature-projections-aggregations.md) |
| **W6** | Optimistic concurrency / row-versioning | M+ | [spec](feature-optimistic-concurrency.md) |
| **W7** | Migrations / schema DDL (Phase A only) | L | [spec](feature-migrations-ddl.md) |
| **W8** | Soft deletes | L | [spec](feature-soft-deletes.md) |
| **W9** | Full-text search | M | [spec](feature-full-text-search.md) |
| **W10** | JSON / array / value-converter column types | L | [spec](feature-column-converters.md) |

Research-derived priority (importance, not sequencing): **E1 ≫ W1 ≈ W2 ≈ W3 > W4 ≈ W5 ≈ W6 > E2 > W7 > W8/W9/W10 ≈ E3.**

---

## The shared "hot spine" — why naive parallelism fails

Nearly every workstream edits the same handful of files. These are where parallel worktrees WILL conflict:

| Shared file | Edited by |
|-------------|-----------|
| `Inquiry.Generators.Shared/Models/ColumnData.cs` + `Abstractions/IColumn.cs` | W5, W6, W7, W8, W10 |
| `Inquiry.Generators.Shared/Abstractions/SqlBuildContext.cs` | W1, W2, W6, W7, W8, E3 |
| `Inquiry.Generators.Shared/Abstractions/SqlBuilder.cs` (+ all 3 `*SqlBuilder.cs`) | W1, W2, W5, W7, W8, W9, E2 (and any new engine must implement new abstract members) |
| `Inquiry.Generators.Shared/StoreProcessor.cs` | W1, W2, W3, W5, W6, W8, W9 |
| `Inquiry.Generators.Shared/StoreOperationEmitter.cs` | W1, W2, W3, W4, W5, W6, W8, W9, W10 |
| `Inquiry.Generators.Shared/EntityProcessor.cs` | W5, W6, W7, W8, W10, (E2 ideally) |
| `Inquiry.Generators.Shared/Models/StoreOperation.cs` + `StoreMethodData.cs` | W1, W2, W3, W5, W6, W8, W9 |
| `Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs` | W1, W2, W6, W7, W8, W9, W10 (all claim "INQ018+") |
| `Inquiry/Pipeline/InquiryRequestPipeline.cs` + `TransactedInquiryRequestPipeline.cs` + `IInquiry.cs` | W3, W4, W5, E2, E3 |
| `Inquiry/Connections/IInquiryConnectionFactory.cs` | E2, W4, E3 |
| Append-only: `Directory.Packages.props`, `Inquiry.slnx`, `samples/Inquiry.Northwind/NorthwindSchema.cs`, `tests/Inquiry.Generators.Tests/InquiryGeneratorTests.cs` (generator array) | E1, E2 (+ W7 for schema) |

---

## Phase 0 — Foundation (land FIRST, serialized, one worktree)

These are small, behavior-preserving seams that turn the hot spine into stable extension points. Doing them up
front is what makes Waves 1–3 safely parallel. **Do not parallelize Phase 0** — these files are the conflict epicenter.

**STATUS: Phase 0 complete** (branch `foundation/phase-0`). During execution, F4 and F6 were **folded into W4**
and F2's per-feature fragments into their workstreams — see the note below — because re-editing the same hot
pipeline/binder lines twice (Phase 0 then W4) is the opposite of merge-clean.

| Foundation | What it does | Unblocks | Status |
|-----------|--------------|----------|--------|
| **F1** | Convert `ColumnData` to **init-only properties** (with `required` polyfills) + document the additive convention: new column metadata is added as init props with defaults, never new ctor params. | W5, W6, W7, W8, W10 | ✅ done |
| **F2** | Shared `AppendWhere(existing, extra)` primitive on `SqlBuilder` so key + concurrency + soft-delete + filter predicates AND-compose through ONE path. (The per-feature *fragments* on `SqlBuildContext` are added additively by each workstream.) | W1, W6, W8, W2 | ✅ done (shipped with F3) |
| **F3** | Convention: new `SqlBuilder` capabilities are `virtual`-with-base-default where dialect-uniform; all WHERE-shaping funnels through `AppendWhere`. Documented on `SqlBuilder`. | W1, W2, W5, W7, W8, W9 | ✅ done |
| **F4** | Defaulted `InitializeCommand(DbCommand)` hook on `IInquiryConnectionFactory` + both pipelines. | E2 (BindByName), W4, E3 | ↪ folded into **W4** (W4 owns the pipeline command-setup edits; E2/E3 rebase) |
| **F5** | Extract materializer helpers (`ReadExpression`/`ReadCallForSpecialType`/`EmitMaterializeBody`) into a shared `MaterializerEmitter` — behavior-preserving, generalized to take a column list so projections reuse it. | W5, W10 | ✅ done |
| **F6** | Emit `DbType` on generated parameters (`DbTypeMapper`) in `StoreOperationEmitter` binders. | W4, W3 | ↪ folded into **W4** (changes emitted code; done with its consumer + tests) |
| **F7** | Diagnostic-ID registry reserving INQ018+ ranges per workstream in `InquiryDiagnosticDescriptors.cs`. | W1, W2, W6, W7, W8, W9, W10 | ✅ done |
| **F8** | Provider append-point checklist → [adding-a-provider.md](adding-a-provider.md). | E1, E2 | ✅ done |

> Net Phase 0 deliverables: F1, F2 (`AppendWhere`), F3, F5, F7, F8 — all behavior-preserving (39/39 generator
> tests green). F4 + F6 move into W4; each WHERE-shaping workstream adds its own `SqlBuildContext` fragment and
> composes it via `AppendWhere`.

---

## Dependency graph

```
Phase 0 (F1–F8)  ── serialized foundation ──┐
                                            │
  ┌─────────────────────────────────────────┴──────────────────────────────────┐
  │                                                                              │
WAVE 1 (parallel worktrees, max isolation)                                       │
  • E1  MySQL provider        (needs F8; rebase for new SqlBuilder members)      │
  • E3  Cloud-compat RUNTIME  (needs F4)                                         │
  • W1  Richer WHERE          (needs F2,F3)  ◀── establishes predicate model     │
  • W4  Prepared statements   (needs F4,F6)                                      │
                                            │
WAVE 2 (parallel; each shares the spine — rebase onto Wave 1)                    │
  • W2  Pagination            (needs F2,F3; keyset reuses W1's comparison, soft-dep)
  • W8  Soft deletes          (needs F2; composes WHERE w/ W1,W6)                │
  • W6  Optimistic concurrency(needs F1,F2; composes WHERE w/ W1,W8)            │
  • W3  Batch & bulk          (needs F6; shares pipeline w/ W4)                  │
  • W5  Projections+aggregates(needs F5; new scalar pipeline path)              │
  • W10 JSON/converters       (needs F5,F1; shares ColumnData)                   │
  • W9  Full-text search      (needs F3; or fold into W1 as a predicate kind)    │
                                            │
WAVE 3 (integrators — depend on several above)                                   │
  • W7  Migrations/DDL        (needs F1; consumes W6 rowversion col + E3 identity)│
  • E2  Oracle provider       (needs F4; must implement every new SqlBuilder member, so land late)
  • E3  Cloud-compat CockroachDB identity DDL (needs W7)                          │
  └──────────────────────────────────────────────────────────────────────────────┘
```

### Hard dependencies (A must land before B)
- F1 → W5, W6, W7, W8, W10  (ColumnData shape)
- F2 → W1, W6, W8           (WHERE composition)
- F4 → E2, W4, E3           (command hook)
- F5 → W5, W10              (MaterializerEmitter)
- F6 → W4                   (DbType metadata; W3 benefits)
- E1 → E3 (Aurora-MySQL only)
- W7 → E3 (CockroachDB identity DDL only)

### Soft dependencies / strong overlap (coordinate, don't necessarily block)
- **W1 ↔ W2 ↔ W6 ↔ W8** all modify WHERE-clause construction. If each funnels through F2's composition primitive they coexist; otherwise they produce malformed WHEREs. **Land W1 first**, then W8/W6, then W2.
- **W5 ↔ W10** both edit materializer generation → both depend on F5; sequence them through the shared `MaterializerEmitter`.
- **W3 ↔ W4** both touch the pipeline command setup; land W4's pipeline hook first, W3 reuses it.
- **E2** must implement every new abstract `SqlBuilder` member (W1/W2/W5/W7/W8/W9). Schedule it **after** the feature workstreams that add abstract members have stabilized, or have those use `virtual`+default (F3) so Oracle inherits.
- **W7 ↔ E3 ↔ W6** all touch `InquiryKeyAttribute`/identity + `ColumnData` — co-design the identity-strategy + rowversion metadata once.

---

## Recommended merge order (one practical linearization)

If you want a single safe sequence rather than managing waves:

1. **Phase 0** (F1, F3, F7 minimum; ideally all of F1–F8) — one PR or a few small ones.
2. **E1 MySQL** — nearly isolated, proves the foundation, highest research priority. Can run concurrently with Phase 0 in its own worktree, rebasing at the end.
3. **W1 Richer WHERE** — establishes the predicate model the WHERE-family builds on.
4. **W4 Prepared statements** + **E3 cloud-compat runtime** — isolated, parallel.
5. **W8 Soft deletes**, then **W6 Optimistic concurrency** — WHERE-composition siblings, serialized through F2.
6. **W2 Pagination** — composes on top of WHERE.
7. **W5 Projections/aggregations** + **W10 JSON/converters** — materializer siblings, serialized through F5.
8. **W3 Batch/bulk**, **W9 Full-text search** — independent additions.
9. **W7 Migrations/DDL** — integrator; consumes W6/E3 metadata.
10. **E2 Oracle** + **E3 CockroachDB identity DDL** — last; Oracle must satisfy all accumulated abstract members.

## Worktree hygiene (applies to every workstream)
- Each spec lists its **SHARED-FILE CONTENTION MAP**. Before starting a worktree, check whether an in-flight
  workstream is editing the same spine file; if so, sequence behind it.
- Keep all edits to `StoreOperation` / `StoreMethodData` / diagnostics **append-only** (new enum arms, new cases,
  new IDs) to make merges textual-only.
- Every workstream is TDD-first: red generator-emission test (assert the exact emitted `const string`) → implement
  → integration test (SQLite always-on; SQL Server/PostgreSQL/MySQL behind the existing opt-in `*FactAttribute`
  env-var gates).
- A new `SqlBuilder` abstract member breaks the build for **all** providers until implemented — prefer `virtual`
  + base default (F3); only use `abstract` when the SQL genuinely has no portable default.

## Out of scope (decided during research)
- **NoSQL/document engines** (Cosmos DB, MongoDB) — don't fit a SQL-generating, JOIN/eager-loading, schema-bound
  model. Excluded.
- **JOIN-based eager loading / lazy loading** — inquiry's separate-query model is the *recommended* high-perf
  pattern; keep it. Not a gap.
- **Migrations Phase B** (schema diff / ALTER / versioning) — delegate to DbUp/FluentMigrator; W7 generates initial
  DDL only.
