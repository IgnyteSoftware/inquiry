# W7 — Migrations / Schema DDL (Phase A only)

> See [README.md](README.md). Depends on: **F1** (ColumnData shape); consumes **W6** rowversion column + **E3** identity strategy. Integrator — land **late**. Size: **L** (Phase A). Contention: **VERY HIGH** (ColumnData/IColumn + SqlBuilder DDL across all providers + attributes).

## 1. Recommended scope
**Build Phase A (DDL generation) only. Defer Phase B (diff/ALTER/versioning); delegate apply/versioning to DbUp/FluentMigrator.** DDL generation fits inquiry perfectly (one more `Build…Sql` per dialect, reusing the metadata pipeline). The target shape already exists as `samples/Inquiry.Northwind/NorthwindSchema.cs` — the goal is to **generate that file's content** and retire the hand-written one. Phase B requires live-DB/snapshot access + history table + runner — antithetical to a source generator and well-served by existing tools.

Phase A ships: richer column/table metadata attributes; `EntityData`/`ColumnData` carry it (+ FK info, currently discovered then discarded); `BuildCreateTableSql` (+ index/FK) per dialect with a type-mapping table; a per-assembly `InquirySchema.g.cs` exposing the DDL string.
**Out of scope (document):** ALTER/diff, versioning, history table, CLI/MSBuild apply, data seeding, view/proc/trigger DDL, DROP.

## 2. Approach (recommended B)
DDL-generation-only + delegate apply. Reject full migrations engine (A — needs runtime DB access, competes with mature tools) and JSON-manifest-only (C — lower value, doesn't retire NorthwindSchema.cs).

## 3. Design
- **New attribute metadata** (optional, additive) on `InquiryColumnAttribute`: `string? SqlType`, `int Length`, `int Precision`, `int Scale`, `bool? IsNullable`, `string? DefaultExpression`, `bool IsUnique`, `bool IsIndexed`, `string? IndexName`. `InquiryKeyAttribute.IsGenerated` already maps to IDENTITY/AUTOINCREMENT/SERIAL. `InquiryTableAttribute`: optional `bool GenerateForeignKeys`. **`InquiryForeignKeyAttribute` already carries `ReferencedTable`/`ReferencedColumn` but they're discarded in `EntityProcessor` — thread them through.**
- **Model:** extend `ColumnData` (after **F1** init-only shape) with the DDL facts + FK reference. Extend `IColumn` with the DDL getters (cross-assembly contract change → all providers recompile). Add `GetNamedInt` to `GeneratorHelpers`. Precompute DDL fragments (column defs, PK, FK, index statements) in `SqlBuildContext`.
- **SqlBuilder (abstract):** `BuildCreateTableSql(ctx)` (idempotent CREATE TABLE + columns + PK + inline FKs), `IReadOnlyList<string> BuildCreateIndexSql(ctx)`, `protected abstract string MapColumnType(IColumn)`.
- **Per-dialect type mapping** (target = exact `NorthwindSchema.cs` output): string→`TEXT`/`NVARCHAR(N)`/`TEXT`; int→`INTEGER`/`INT`/`INTEGER`; decimal(p,s)→`NUMERIC`/`DECIMAL(p,s)`/`NUMERIC(p,s)`; bool→`INTEGER`/`BIT`/`BOOLEAN`; DateTime→`TEXT`/`DATETIME`/`TIMESTAMP`; byte[]→`BLOB`/`VARBINARY(MAX)`/`BYTEA`; Guid→`TEXT`/`UNIQUEIDENTIFIER`/`UUID`. Generated keys: SQLite `INTEGER PRIMARY KEY AUTOINCREMENT`, SqlServer `INT IDENTITY(1,1) PRIMARY KEY`, PG `SERIAL PRIMARY KEY`. Idempotency: SQLite/PG `CREATE TABLE IF NOT EXISTS`; SqlServer `IF OBJECT_ID(...) IS NULL BEGIN … END;`. (byte[] detection: `TypeData` has no blob flag — match display name or add `IsByteArray` to `TypeData`.)
- **Emission:** new `SchemaEmitter`; in `InquiryGeneratorBase.Execute`, gated by dialect ownership, build a `SqlBuildContext` per entity, concatenate CREATE TABLE + indexes ordered by FK dependency (topological sort; handle self-FK like `Employees.ReportsTo`), emit `InquirySchema.g.cs` exposing `public static class InquiryGeneratedSchema { public static readonly string Ddl = "…"; }`. Per-assembly, gated so multi-provider builds don't collide.
- **Apply/versioning (delegate):** no history table/runner. Document: (1) optional tiny runtime `IInquirySchema.EnsureCreatedAsync(DbConnection)` labeled "first-run/dev only"; (2) feed the generated DDL into DbUp as `0001_initial` (recommended production path).

## 4. Implementation steps (TDD)
1. Attribute options. *Verify:* entity using each compiles.
2. `GeneratorHelpers.GetNamedInt`. *Verify:* unit test.
3. Thread metadata into `ColumnData`/`EntityData` incl. FK refs; populate in `DiscoverColumns`. *Verify:* via DDL (step 6); value-equality intact.
4. Extend `IColumn` + `SqlBuildContext` DDL fragments. *Verify:* existing CRUD tests still pass.
5. `SqlBuilder.BuildCreateTableSql`/`MapColumnType` abstract (build fails until all 3 implement — TDD forcing function).
6. Per-dialect impl, one at a time. **Golden test:** generated DDL string-equals each block of `NorthwindSchema.cs` (whitespace-normalized) — SQLite, then SqlServer, then PostgreSql.
7. `SchemaEmitter` + wire into `Execute`, gated by ownership. *Verify:* exactly one schema source, none when ownership NotMine/AmbiguousFollower.
8. Topological FK ordering. *Verify:* referenced tables first; self-FK no deadlock.
9. Round-trip integration: execute generated DDL, run an existing CRUD test against it (SQLite in-memory; SqlServer/PG gated).
10. **Retire `NorthwindSchema.cs`** → use `InquiryGeneratedSchema.Ddl`. (Headline deliverable + ultimate regression.)
11. Incremental-cache test (`WithTrackingName` on schema output).

## 5. Shared-file contention map
- **MODIFY (high):** `Entities/InquiryColumnAttribute.cs` + `InquiryKeyAttribute.cs` + `InquiryTableAttribute.cs`, `Models/ColumnData.cs` (**biggest collision**), `Abstractions/IColumn.cs` (cross-assembly), `Abstractions/SqlBuilder.cs` (DDL methods), `Abstractions/SqlBuildContext.cs`, `EntityProcessor.cs`, `InquiryGeneratorBase.cs`, `Infrastructure/GeneratorHelpers.cs` (GetNamedInt), 3 `*SqlBuilder.cs` (DDL + type mapping), `samples/Inquiry.Northwind/NorthwindSchema.cs` (replaced).
- **ADD:** `SchemaEmitter.cs`, optional `IndexDescriptor.cs`/`ForeignKeyDescriptor.cs`, optional runtime `Schema/IInquirySchema.cs` + `EnsureCreatedAsync`, golden + round-trip tests.

## 6. Cross-workstream dependencies & sequencing
- **`ColumnData`/`IColumn` is the shared spine** — W6, W8, W5, W10 also add fields. **Land F1 first**; additive members; every `IColumn` change recompiles all providers.
- **E3 CockroachDB identity** collides with §3 generated-key mapping (`SERIAL`/IDENTITY/AUTOINCREMENT → Cockroach `unique_rowid()`). Identity-strategy attribute metadata should precede or co-design with DDL key emission.
- **W6 rowversion** → DDL must emit the right physical type (`ROWVERSION` on SqlServer). Co-design the flag.
- Any new dialect must now implement the DDL abstract methods — raises the bar for E1/E2.
- **Recommended global order:** freeze ColumnData/IColumn (F1) → land identity-strategy metadata (E3/W6) → land this consuming both.

## 7. Test strategy
Golden/snapshot DDL = generated equals each `NorthwindSchema.cs` block (whitespace-normalized) — the executable spec. Generator-harness tests for schema presence, ownership gating, FK ordering, self-FK, incremental cache. Per-dialect `MapColumnType` unit tests (length/precision/nullable/blob/guid/enum). Round-trip integration (SQLite always; SqlServer/PG gated). No-regression on existing CRUD after IColumn/SqlBuildContext changes.

## 8. Risks / open questions
- **Scope creep is #1** — write the Phase-B deferral into the PR; any runtime helper labeled "first-run, not migrations".
- Length/precision metadata unavoidable (`NVARCHAR(40)` vs `MAX`, `DECIMAL(19,4)`) — pick sensible defaults so most columns need no annotation.
- Idempotency dialect divergence (SqlServer `IF OBJECT_ID` vs `IF NOT EXISTS`) — keep per-dialect.
- `IColumn` change ripples to all providers — sequence early.
- `SqlType` override is dialect-specific — defer in v1, rely on length/precision/inference.
- FK ordering in-assembly only (cross-assembly out of scope).
- blob detection — display-name match or add `IsByteArray` to `TypeData`.

## 9. Size: **L** (Phase A all 3 dialects + integration). **M** for a SQLite-only first slice. Optional `EnsureCreated` helper **S**. Phase B = **L+**, deferred/delegated.
