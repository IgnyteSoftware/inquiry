# W6 — Optimistic Concurrency / Row-Versioning

> See [README.md](README.md). Depends on: **F1** (ColumnData shape), **F2** (WHERE composition). WHERE-family sibling of **W1**/**W8** — sequence after W1. Size: **M+**. Contention: **HIGH** (ColumnData/IColumn + SqlBuildContext + all provider UPDATE/DELETE).

## 1. Feature summary & surface
Today UPDATE/DELETE are unconditional `WHERE key = @key` (last-write-wins). Add a concurrency token so generated UPDATE/DELETE append `AND <token> = @token` and (ORM-managed) `SET …, <token> = <token> + 1`.
```csharp
[InquiryConcurrencyToken] public int Version { get; set; }              // ORM-managed numeric
[InquiryColumn("rowver"), InquiryConcurrencyToken(DatabaseGenerated = true)] public byte[] RowVersion { get; set; } // SQL Server rowversion
```
`InquiryConcurrencyTokenAttribute : InquiryColumnAttribute` (so it's discovered as a column, like `InquiryKeyAttribute`). **Conflict = 0 rows affected.** Default: `bool`-returning UPDATE/DELETE return `false` (backward compatible w/ "not found"). Opt-in `InquiryOptions.ThrowOnConcurrencyConflict` converts a 0-row mutation on a token entity to `InquiryConcurrencyException` (and turns a `null` `ReturnEntity` result into a throw, since null otherwise conflates stale-vs-deleted).

## 2. Approach (recommended C + opt-in throw)
- Token strategy **C**: ORM-managed numeric (portable default, all 4 dialects) + SQL Server `rowversion` DB-managed via `DatabaseGenerated=true`; diagnose `DatabaseGenerated` on dialects that can't support it (INQ018). PostgreSQL `xmin` deferred.
- Failure surfacing: **opt-in throw, default off** (preserves the existing `bool` contract; generator emits the throw branch only for token entities).

## 3. Design
- **Attribute:** `src/Inquiry/Entities/InquiryConcurrencyTokenAttribute.cs` (derives `InquiryColumnAttribute`, `bool DatabaseGenerated`).
- **EntityProcessor:** `DiscoverColumns` adds a 3rd probe `GetEntityAttribute(property, "InquiryConcurrencyTokenAttribute")` folded into the column fallback; read `DatabaseGenerated`. Validate ≤1 token (INQ018), token≠key (INQ019).
- **ColumnData/IColumn (F1):** add `bool IsConcurrencyToken`, `bool IsDatabaseGeneratedToken`. (Init-only after F1; one construction site in `EntityProcessor`.)
- **SqlBuildContext (F2 seam):** `IColumn? ConcurrencyToken`; `ConcurrencyWhereClause` (`" AND " + Quote(token) + " = " + Param(token)`); `ConcurrencyVersionSet` (`Quote+ " = " + Quote + " + 1"`, ORM-managed only); `SetClausesWithVersion` = SetClauses + version set. Adjust `InsertableColumns` to exclude `IsDatabaseGeneratedToken`; `SetClauses` to exclude `IsConcurrencyToken`. This keeps all provider UPDATE methods uniform: swap `SetClauses`→`SetClausesWithVersion`, append `ConcurrencyWhereClause`.
- **SqlBuilder (3 providers):** `BuildUpdateSql`/`BuildUpdateReturningSql`/`BuildDeleteByKeySql` consume the new fragments. RETURNING/OUTPUT already projects all columns → new/incremented version flows back **free**. INSERT: ORM token is ordinary insertable; DB-managed excluded via `InsertableColumns`. INQ018 for DB-managed on SQLite (in `StoreProcessor.Emit` where dialect is known).
- **Binder (`StoreOperationEmitter`):** UPDATE WHERE references `@Version` (original value) — ORM token (`!IsGenerated`) already bound; DB-managed token must be excluded from the INSERT binder branch (`&& !IsDatabaseGeneratedToken`).
- **Runtime:** new `src/Inquiry/InquiryConcurrencyException.cs`; `bool ThrowOnConcurrencyConflict` on `IInquiry`/`DefaultInquiry`. Generator (gated on `entity has token`) emits at the store call site: `var _rows = await Inquiry.ExecuteAsync(...); if (_rows == 0 && Inquiry.ThrowOnConcurrencyConflict) throw new InquiryConcurrencyException(...); return _rows > 0;` — **no pipeline change**. Non-token entities emit byte-identical code to today.

## 4. Implementation steps (TDD)
1. Attribute + ColumnData/IColumn flags + update construction site. *Verify:* solution compiles.
2. EntityProcessor discovery + INQ018/019. *Verify:* generator test (token flagged, `DatabaseGenerated` honored, diagnostics fire).
3. SqlBuildContext fragments + InsertableColumns/SetClauses fixes. *Verify:* snapshot SQL shows new WHERE/SET all 3 dialects.
4. SqlBuilder UPDATE/DELETE/INSERT edits + INQ018 unsupported DB-managed. *Verify:* exact SQL (`AND "Version" = @Version`, `SET …, "Version" = "Version" + 1`).
5. Binder exclusion for DB-managed token. *Verify:* DB-managed absent from INSERT binder, present in UPDATE WHERE.
6. Runtime exception + option + conditional throw emit. *Verify:* SQLite integration (concurrent update false/throws when option set; version increments; stale delete fails).
7. Per-dialect integration (SQLite mandatory; SqlServer rowversion via OUTPUT; PG ORM-managed).

## 5. Shared-file contention map
- **MODIFY (shared):** `Models/ColumnData.cs`, `Abstractions/IColumn.cs`, `Abstractions/SqlBuildContext.cs`, `EntityProcessor.cs`, `StoreProcessor.cs` (INQ018 emit-time), `StoreOperationEmitter.cs` (binder exclusion + conditional throw), `Diagnostics/InquiryDiagnosticDescriptors.cs`, `IInquiry.cs`/`DefaultInquiry.cs`, 3 `*SqlBuilder.cs` (UPDATE/DELETE).
- **NOT modified:** `InquiryRequestPipeline.cs` (throw emitted at store call site).
- **ADD:** `Entities/InquiryConcurrencyTokenAttribute.cs`, `InquiryConcurrencyException.cs`, tests.

## 6. Cross-workstream dependencies & sequencing
- **WHERE-clause collision (highest):** W8 soft-delete (adds `AND is_deleted=0`, delete→update) and W1 (filtered updates) edit the same `BuildUpdateSql`/`BuildDeleteByKeySql` + SqlBuildContext WHERE. **All must compose through F2 named fragments** (`KeyWhere + ConcurrencyWhere + SoftDeleteWhere`), not per-provider rewrites. Establish that convention here (or in F2).
- **ColumnData collision:** projections/migrations/JSON also add fields → land **F1 first**; append new members.
- **RETURNING overlap:** projections narrowing RETURNING columns must keep the token projected.
- **Suggested:** F1 + F2 → this workstream's builder edits → W8 on top via the fragment convention.

## 7. Test strategy
Generator: discovery, diagnostics (INQ018 dup/unsupported-dialect, INQ019 token==key), exact SQL per dialect (UPDATE/UPDATE-RETURNING/DELETE/INSERT). SQLite integration: success increments + returns true; stale → false/throws; `ReturnEntity` returns incremented row or throws; stale delete fails; non-token unchanged. SqlServer rowversion round-trip via OUTPUT. Regression: zero diff for non-token entities.

## 8. Risks / open questions
- `@Version` is the original value (WHERE); SET uses unparameterized `version + 1` → single `@Version` is unambiguous. Confirm no builder emits `version = @Version` in SET.
- `ReturnEntity` + conflict = null ambiguity → throw-on-conflict resolves; document.
- Upsert + token: unclear semantics → diagnose as unsupported v1.
- DB-managed token type validation is dialect-specific (emit-time).
- PostgreSQL `xmin` (system column) deferred; v1 diagnoses `DatabaseGenerated` on PG.
- Option granularity: global v1.

## 9. Size: **M+** — uniform SQL changes via the SqlBuildContext fragment seam (RETURNING read-back free), but weight in the ColumnData ripple + 3 diagnostics + runtime exception/option plumbing + conditional throw emit + cross-dialect tests + rowversion edge.
