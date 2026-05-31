# W8 — Soft Deletes

> See [README.md](README.md). Depends on: **F1** (ColumnData), **F2** (WHERE composition). WHERE-family sibling of **W1**/**W6** — land after W1. Size: **L**. Contention: **HIGH** (modifies WHERE in every select builder + delete→update + new op).

## 1. Feature summary & surface
Mark a soft-delete column; the generator (a) rewrites `[InquiryDeleteOneByKey]` into `UPDATE … SET <flag> WHERE key=@key`, (b) appends a default filter (`is_deleted = 0` / `deleted_at IS NULL`) to every SELECT.
```csharp
[InquirySoftDelete] public bool IsDeleted { get; set; }          // flag form
[InquirySoftDelete] public DateTime? DeletedAt { get; set; }     // timestamp form
[InquiryDeleteOneByKey]                 // → UPDATE SET IsDeleted = 1 WHERE Id = @Id
[InquiryDeleteOneByKey(HardDelete = true)]   // → DELETE
[InquirySelectAll]                      // → ... WHERE IsDeleted = 0
[InquirySelectAll(IncludeDeleted = true)]    // → unfiltered
[InquiryRestoreOneByKey]                // → UPDATE SET IsDeleted = 0 WHERE Id = @Id
```
The property still needs `[InquiryColumn]`; `[InquirySoftDelete]` is an orthogonal marker (representation inferred from CLR type: bool→flag, nullable DateTime/DateTimeOffset→timestamp; else diagnostic). `@now` sourced from the DB (`CURRENT_TIMESTAMP`/`GETUTCDATE()`/`now()`) so the binder signature stays single-key.

## 2. Approach (recommended B)
Centralize WHERE composition in `SqlBuildContext` (F2); providers consume precomputed fragments + emit dialect literals via small virtuals (`SoftDeleteFalseLiteral`, `CurrentTimestampExpression`). Reject A (per-provider duplication, drift) and C (SQL assembly leaking out of `SqlBuilder`).

## 3. Design
- **Attribute:** `src/Inquiry/Entities/InquirySoftDeleteAttribute.cs`.
- **Store attrs:** `InquiryDeleteOneByKeyAttribute` += `bool HardDelete`; select attrs (`SelectAll`/`SelectOneByKey`/`SelectAllByField`/eager variants) += `bool IncludeDeleted`; new `InquiryRestoreOneByKeyAttribute`.
- **ColumnData/IColumn (F1):** add `SoftDeleteKind SoftDelete {None,BooleanFlag,Timestamp}`. `EntityProcessor.DiscoverColumns` detects `[InquirySoftDelete]`, infers kind from type; validate ≤1 (diagnostic). `EntityData` optionally caches the soft-delete column.
- **StoreMethodData:** add `IncludeDeleted`, `HardDelete` (read via `GetNamedBool`). New `StoreOperation.RestoreOneByKey` (route like DeleteOneByKey: `Task<bool>`, key params).
- **SqlBuildContext (F2 seam):** `SoftDeleteColumn`; `SoftDeleteActivePredicate` (`"IsDeleted" = 0` / `"DeletedAt" IS NULL`); `SoftDeleteSetClause` (`= 1` / `= CURRENT_TIMESTAMP`); `SoftDeleteRestoreSetClause` (`= 0` / `= NULL`).
- **SqlBuilder:** virtuals `SoftDeleteTrueLiteral`/`FalseLiteral` (default `1`/`0`), `CurrentTimestampExpression` (default `CURRENT_TIMESTAMP`), + `AppendWhere(existing, extra)` helper (F2). New **concrete** (non-abstract) `BuildSoftDeleteByKeySql` + `BuildRestoreByKeySql` (no OUTPUT/RETURNING needed for `Task<bool>`) — so the delete/restore path needs **no per-provider edit**. Select methods AND-combine `SoftDeleteActivePredicate`.
- **Composition:** select methods change `... WHERE KeyWhereClause` → `... + AppendWhere(KeyWhereClause, SoftDeleteActivePredicate)`; SelectAll gains a WHERE only when the predicate exists. `IncludeDeleted` handled in `StoreProcessor.Emit` by constructing a context with soft-delete suppressed (keeps `SqlBuilder` select signatures stable).
- **Delete→update:** `StoreProcessor.Emit` chooses `BuildDeleteByKeySql` (HardDelete or no soft-delete col) vs `BuildSoftDeleteByKeySql`. The emitter `DeleteOneByKey` case is unchanged (binds key, `ExecuteAsync`, `> 0` — works for UPDATE). `RestoreOneByKey` reuses `EmitFastExecuteFromKeys` + a `_sqlRestoreByKey` const.
- **Per-dialect:** SQLite/SqlServer bool `1`/`0`; PostgreSQL override `TRUE`/`FALSE`. Timestamp: SQLite `CURRENT_TIMESTAMP`, SqlServer `GETUTCDATE()`, PG `now()`.

## 4. Implementation steps (TDD)
1. Attributes. *Verify:* default-value tests.
2. Model + discovery + diagnostics (wrong type, >1). *Verify:* generator test column flagged; string-property diagnostic.
3. SqlBuildContext fragments + SqlBuilder virtuals/concrete soft-delete & restore builders. *Verify:* `_sqlSelectAll` contains `WHERE "IsDeleted" = 0`; `_sqlDeleteByKey` is now `UPDATE … SET "IsDeleted" = 1 …`.
4. Select WHERE composition in all 3 providers. *Verify:* per-dialect select-string tests; PG `TRUE/FALSE`.
5. StoreProcessor wiring (delete vs soft vs hard; `_sqlRestoreByKey`; suppress filter for IncludeDeleted; RestoreOneByKey flags). *Verify:* `HardDelete=true` still `DELETE`; `IncludeDeleted=true` no WHERE; restore const present.
6. Emitter RestoreOneByKey case. *Verify:* body binds key + returns `> 0`.
7. SQLite integration (flag form): soft-delete hides from SelectAll/SelectByKey, visible via IncludeDeleted, restorable; HardDelete removes. Mirror to PG/SqlServer.
8. Timestamp-form integration (`DateTime? DeletedAt`): `IS NULL` filter + DB-sourced timestamp.

## 5. Shared-file contention map
- **MODIFY (highest):** `Abstractions/SqlBuilder.cs` (virtuals + 2 concrete builders + `AppendWhere`), `Abstractions/SqlBuildContext.cs` (**central WHERE-composition point — shared w/ W1/W6**), `Abstractions/IColumn.cs`, `Models/ColumnData.cs`, `Models/EntityData.cs`, `Models/StoreOperation.cs`, `Models/StoreMethodData.cs`, `EntityProcessor.cs`, `StoreProcessor.cs` (GetOperation/HasSupported*/ExtractMethod/Emit), `StoreOperationEmitter.cs` (RestoreOneByKey), `Diagnostics/InquiryDiagnosticDescriptors.cs`, 3 `*SqlBuilder.cs` (3 select methods each; delete/restore handled in base).
- **MODIFY (`src/Inquiry/Stores/`):** delete attr (+HardDelete) + 5 select attrs (+IncludeDeleted).
- **ADD:** `Entities/InquirySoftDeleteAttribute.cs`, `Stores/InquiryRestoreOneByKeyAttribute.cs`, fixtures + integration tests.

## 6. Cross-workstream dependencies & sequencing
- **Among the most contended** — changes WHERE composition in every select builder + operation routing. **W1**, **W6** also modify generated WHERE. **All three must funnel through F2's `AppendWhere`/named fragments** (`KeyWhere + ConcurrencyWhere + SoftDeleteWhere + filter`) — implement F2 first (or jointly). Sequence: F2 + F1 → W1 → W8 → W6, each plugging into the shared primitive.
- **ColumnData (W5/W7/W10):** coordinate the record change in one F1 merge; append members.
- **Eager loading** builds child SQL via `BuildSelectByField`/`BuildSelectAll` → soft-delete filtering auto-applies to children (likely desired; confirm; parent `IncludeDeleted` doesn't propagate).
- Operation-enum/switch additions append-only.

## 7. Test strategy
Generator string tests (filtered SELECT, soft UPDATE delete, HardDelete literal DELETE, IncludeDeleted unfiltered, restore const, PG TRUE/FALSE, SqlServer GETUTCDATE). Diagnostics (wrong type, >1 column). SQLite integration round-trip (both forms). Cross-provider parity (PG/SqlServer). Regression: no-soft-delete entities emit identical SQL.

## 8. Risks / open questions
- `IncludeDeleted` plumbing through immutable `Build*` signatures — recommend per-statement context with soft-delete suppressed (keeps signatures stable) over a bool param on all providers.
- `@now` is DB-clock (not app) — documented; app-provided would change the binder signature (out of scope).
- Composite keys reuse `KeyWhereClause` (already safe).
- Upsert/Update could resurrect a soft-deleted row — out of scope, doc note.
- Diagnostic IDs coordinated (F7).

## 9. Size: **L** — deep cross-cutting WHERE change across all 3 providers' selects + delete→update routing + two representations + restore op + 3 opt-out flags + model/IColumn changes + per-dialect integration; coordination with two other WHERE-modifying workstreams adds cost.
