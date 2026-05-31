# W3 — Batch & Bulk Operations

> See [README.md](README.md). Depends on: **F6** (DbType metadata helps); shares pipeline w/ **W4**. Size: **L**. Contention: **HIGH** (StoreProcessor/Emitter/pipeline + new bulk abstraction + per-provider executors).
> Note: only 3 providers exist today (SQLite/SqlServer/PostgreSql). MySQL/Oracle bulk paths are future; the abstraction is designed so they plug in with no shared-file change.

## 1. Feature summary & surface
Two families on `IEnumerable<T>`/`IReadOnlyList<T>`:
```csharp
[InquiryInsertAll(BatchSize = 500)] public partial Task<int> InsertAllAsync(IEnumerable<Order> orders, CancellationToken ct = default);
[InquiryUpdateAll]                  public partial Task<int> UpdateAllAsync(IReadOnlyList<Order> orders, CancellationToken ct = default);
[InquiryDeleteAllByKey]             public partial Task<int> DeleteAllAsync(IEnumerable<Guid> keys, CancellationToken ct = default);
[InquiryUpsertAll(BatchSize = 200)] public partial Task<int> MergeAllAsync(IEnumerable<Order> orders, CancellationToken ct = default);
[InquiryBulkInsert]                 public partial Task    BulkInsertAsync(IEnumerable<Order> orders, CancellationToken ct = default);
```
Batch returns total affected `int`; bulk returns `Task`. `BatchSize` default ~1000, capped so `BatchSize × insertableCols ≤ 2100` (SQL Server param limit).

## 2. Approach (recommended A — fixed-chunk template + runtime remainder)
The tension: batch needs runtime-variable N rows, but inquiry bakes const SQL. **A:** emit ONE `const string` for a full chunk of exactly `BatchSize` rows (`VALUES (@p0_*),(@p1_*),…`); the generated body chunks input, full chunks reuse the const (hot path = 100% const, DB caches plan), the trailing partial chunk builds its VALUES + params at runtime (bounded, `< BatchSize`). Reject B (always-runtime-built, abandons const SQL) and C (loop single-row = N round-trips, not batching).
Bulk is orthogonal (no SQL): provider-native `SqlBulkCopy` / Npgsql binary `COPY`.

## 3. Design
- **Attributes** (`src/Inquiry/Stores/`): `InquiryInsertAllAttribute`, `InquiryUpdateAllAttribute`, `InquiryDeleteAllByKeyAttribute`, `InquiryUpsertAllAttribute` (each `int BatchSize`), `InquiryBulkInsertAttribute`.
- **Generator:** add `InsertAll/UpdateAll/DeleteAllByKey/UpsertAll/BulkInsert` to `StoreOperation`; `StoreMethodData` gains `BatchSize` + element-type FQN. `StoreProcessor`: `GetOperation` arms, `ExtractMethod` reads BatchSize/element type, `HasSupportedReturnType` (batch→`Task<int>`, bulk→`Task`), `HasSupportedParameters` (`IEnumerable<T>`/`IReadOnlyList<T>` of entity, or single key type for DeleteAllByKey), `Emit` emits `_sqlInsertAll_<n>` chunk consts.
- **`StoreOperationEmitter`:** `EmitBatchInsert/Update/DeleteByKey/Upsert` + `EmitBulkInsert`; batch binder loops chunk binding `@p{row}_{col}` (reuse `AppendBinderLambda`/`BuildParameterValueExpression` coercion).
- **SqlBuilder (F3):** `virtual BuildBatchInsertSql(ctx, rowCount)`, `BuildBatchDeleteByKeySql(ctx, rowCount)` (single-key `IN`), `BuildBatchUpdateSql`, `BuildBatchUpsertSql` (SqlServer `MERGE … USING (VALUES …)`; PG/SQLite `… ON CONFLICT`). `SqlBuildContext` helper `BuildRowParameters(rowIndex)` → `@{prop}_{rowIndex}`.
- **Bulk (runtime, provider-specific):** new `src/Inquiry/Bulk/IInquiryBulkCopyExecutor.cs` (`BulkInsertAsync<T>(connection, transaction, table, columns, rows, valueAccessor, batchSize, timeout, ct)`) + `BulkColumnMap`. Pipeline + `IInquiry` gain `BulkInsertAsync<T>` that reuses the existing connection/ambient-transaction lifecycle and delegates to the injected executor. Provider impls (NEW, not shared): `SqlServerBulkCopyExecutor` (`SqlBulkCopy`), `PostgreSqlBulkCopyExecutor` (`BeginBinaryImportAsync`), `SqliteBulkCopyExecutor` (no native bulk → batch-backed fallback). Registered via each provider's `Add…` DI extension. Generator emits the call with const table + column-map + static accessor delegate. **Add raw (unquoted) `SqlBuildContext.RawTableName`/`RawSchema`** (bulk APIs need raw names).
- **Batch execution:** add `ExecuteBatchAsync<TArgs>` to the pipeline — open one connection, loop chunks rebinding params, sum affected (amortizes connection-open).

## 4. Implementation steps (TDD)
1. Attributes + enum + model. *Verify:* builds; Inquiry.Tests green.
2. Generator discovery (red→green) — test `[InquiryInsertAll(BatchSize=3)]` emits `_sqlInsertAll_3` multi-row VALUES + `Task<int>` body. *Verify.*
3. SqlBuilder batch methods (base + 3 overrides). *Verify:* per-dialect emitted SQL.
4. `StoreOperationEmitter` batch bodies + pipeline `ExecuteBatchAsync`. *Verify:* generated body; Inquiry.Tests chunk math (7 rows / BatchSize 3 → 2 full + remainder 1; affected summed).
5. Bulk abstraction + pipeline/facade plumbing + raw table name. *Verify:* stub-executor forwards connection/transaction/columns/accessor.
6. Bulk generator emission. *Verify:* body calls `Inquiry.BulkInsertAsync`, no SQL const.
7. Provider executors + DI. *Verify:* per-provider integration tests.

## 5. Shared-file contention map
- **MODIFY (shared, highest):** `Models/StoreOperation.cs`, `Models/StoreMethodData.cs`, `StoreProcessor.cs` (GetOperation/ExtractMethod/HasSupported*/Emit), `StoreOperationEmitter.cs`, `Abstractions/SqlBuilder.cs`, `Abstractions/SqlBuildContext.cs` (BuildRowParameters, raw table), `IInquiry.cs`/`DefaultInquiry.cs`, `Pipeline/IInquiryRequestPipeline.cs` + `InquiryRequestPipeline.cs` + `TransactedInquiryRequestPipeline.cs`.
- **MODIFY (per provider, parallelizable):** 3 `*SqlBuilder.cs` (batch SQL), 3 DI extensions (register executor).
- **ADD:** 5 attributes, `Bulk/IInquiryBulkCopyExecutor.cs` + `BulkColumnMap.cs`, 3 `*BulkCopyExecutor.cs`.

## 6. Cross-workstream dependencies
- **Upsert (existing):** batch upsert reuses each provider's upsert strategy; the single-row `DatabaseMaySupplyKey` IF-branch does NOT generalize to multi-row — batch upsert requires explicit/non-generated key or per-row fallback. Coordinate if any upsert refactor is in flight.
- **MySQL/Oracle (future):** `IInquiryBulkCopyExecutor` is designed so they add only a `*BulkCopyExecutor.cs` + SqlBuilder overrides — no shared change.
- **W4 prepared statements:** the full-chunk const template is an ideal prepare candidate; both touch pipeline command setup → land W4's pipeline hook first, batch reuses it; share `InquiryOptions` + capability interface.
- **Sequencing:** land steps 1–4 (batch, shared core) serialized before parallelizing provider bulk executors (step 7).

## 7. Test strategy
Emitted-SQL (batch) per op + BatchSize: exact `_sqlInsertAll_3` const + body (chunk loop, remainder, `Task<int>`, summed). Bulk: body calls executor, no SQL const. Diagnostics (wrong return type, non-enumerable param, composite-key DeleteAllByKey). Runtime chunking via fake pipeline (count, remainder, param names/values, transaction participation, summation). Bulk integration per provider (insert N, read back, assert count + transaction rollback empties table; SQLite fallback).

## 8. Risks / open questions
- const-vs-N: cap `BatchSize × insertableCols ≤ 2100` → diagnostic or auto-clamp (decide).
- Atomicity: multi-chunk batch atomic only inside `BeginTransactionAsync` — document or auto-wrap; bulk executor must honor ambient `DbTransaction`.
- Bulk + generated keys/returning: `SqlBulkCopy`/`COPY` don't return keys → `[InquiryBulkInsert]` rejects `ReturnEntity`.
- SQLite no native bulk → confirm fallback (batch-backed vs diagnostic).
- Raw vs quoted table name threading.

## 9. Size: **L** — batch alone (shared core + 3 SqlBuilders + pipeline `ExecuteBatchAsync` + tests) is M; bulk adds a runtime abstraction + facade across 3 files + 3 executors w/ integration → L. Shared generator core is the critical-path bottleneck; provider executors parallelize once the abstraction lands.
