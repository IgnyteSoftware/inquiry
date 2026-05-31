# W2 — ORDER BY + Pagination (Offset + Keyset/Cursor)

> See [README.md](README.md). Depends on: **F2**, **F3**; soft-dep on **W1** (keyset comparison — but self-contains its own keyset predicate to avoid blocking). Size: **L** (ORDER BY alone is S). Contention: **HIGH** (SqlBuilder + all providers).

## 1. Feature summary & surface
Today no ORDER BY / LIMIT / OFFSET. Add three capabilities on existing select attributes + a new keyset attribute.
```csharp
[InquirySelectAll(OrderBy = "Name ASC, Id DESC")]                              // ordering
[InquirySelectAll(OrderBy = "Id ASC", Paged = true)]                           // offset paging (offset,limit params)
[InquiryKeysetPage("Id", Direction = KeysetDirection.Forward)]                 // keyset
public partial Task<InquiryPage<User,long>> PageAsync(long? afterId, int pageSize, CancellationToken ct = default);
```
`OrderBy` parsed at compile time into `(field, dir)`, fields resolved via `FindColumn`, quoted via `QuoteIdentifier` (unknown field → diagnostic; no injection). Offset paging requires `OrderBy` (mandatory all dialects for deterministic + SQL-Server-`OFFSET/FETCH`). Keyset returns `readonly struct InquiryPage<TEntity,TCursor>` (`Items`, `NextCursor`, `HasMore`).

## 2. Approach (recommended A)
Named args (`OrderBy`, `Paged`) on `InquirySelectAllAttribute`/`InquirySelectAllByFieldAttribute` (consistent with existing `ReturnEntity`); keyset gets its own `StoreOperation.KeysetPage` + `[InquiryKeysetPage]` (changes return shape + predicate). Reject DSL string (B) and split-everything attributes (C).

## 3. Design
- **Shared model:** new `Models/OrderingData.cs` (`record OrderItem(string PropertyOrColumn, bool Descending)`, `Pagination {None,Offset,Keyset}` enum); add `EquatableArray<OrderItem> OrderBy`, `Pagination`, `EquatableArray<string> KeysetFields`, `bool KeysetDescending` to `StoreMethodData`. Add `KeysetPage` to `StoreOperation`.
- **New `Abstractions/SqlSelectOptions.cs`** value object carrying already-resolved+quoted ORDER BY list, offset/limit/cursor param names, keyset op (`>`/`<`). Resolution/quoting happens in `StoreProcessor` (has columns + builder); builders only assemble strings.
- **SqlBuilder (F3):** `virtual string BuildOrderByClause(SqlSelectOptions)` (uniform default), `abstract string BuildPaginationClause(SqlSelectOptions)` (dialect-specific), `virtual string BuildKeysetPredicate(...)` (row-value default; SqlServer OR-form override). `StoreProcessor.Emit` composes `BuildSelectAllSql + BuildOrderByClause + BuildPaginationClause`. Existing select signatures untouched.
- **Per-dialect pagination (load-bearing):** SQLite/PG `LIMIT @limit OFFSET @offset`; **SQL Server `OFFSET @offset ROWS FETCH NEXT @limit ROWS ONLY` (requires ORDER BY)**; Oracle (future) same as SqlServer. Keyset: `WHERE (@cursor IS NULL OR Id > @cursor) ORDER BY Id ASC LIMIT @pageSize` (single const serves first + later pages). Multi-column keyset: row-value `(a,b) > (@c0,@c1)` on PG/SQLite; SqlServer lacks row-value `>` → lexicographic OR-form `(a > @c0) OR (a = @c0 AND b > @c1)`.
- **Binding:** offset/limit/cursor are scalar params via `QueryListAsync<TEntity,TArgs,TMaterializer>` with a value-tuple `TArgs` + emitted binder lambda. New small helper binds synthetic `@__offset`/`@__limit`/`@__cursor`/`@__pageSize` (not entity columns).
- **Return:** ORDER BY/offset keep existing return types; keyset adds `src/Inquiry/Paging/InquiryPage.cs` (struct; `HasMore` via requesting `pageSize+1` then trimming; `NextCursor` from last row's key properties — no extra query).

## 4. Implementation steps (TDD)
1. ORDER BY parse + model. *Verify:* generator test `…ORDER BY "Name" ASC, "Key" DESC`; unknown-field diagnostic.
2. `BuildOrderByClause` default + wire into `Emit`. *Verify:* per-dialect emitted SQL + integration ordered results.
3. Diagnostic + validation: offset paging requires ORDER BY + 2 int params (INQ018). *Verify:* diagnostic + param-shape tests.
4. `BuildPaginationClause` per dialect. *Verify:* SQLite/PG `LIMIT…OFFSET`, SqlServer `OFFSET…FETCH`.
5. Offset binder emission. *Verify:* generated body + integration page boundaries/last partial page.
6. Keyset op + `[InquiryKeysetPage]` + `InquiryPage<,>`. *Verify:* return-type validation; struct unit tests.
7. `BuildKeysetPredicate` (row-value default + SqlServer OR-form). *Verify:* per-dialect emitted SQL single+multi-column; integration forward paging via round-tripped `NextCursor`, null first-page, `HasMore`.

## 5. Shared-file contention map
- **MODIFY (shared):** `Models/StoreMethodData.cs`, `Models/StoreOperation.cs`, `StoreProcessor.cs` (ExtractMethod parse, Emit compose, validation, GetOperation), `StoreOperationEmitter.cs` (scalar-param binder, KeysetPage case), `Abstractions/SqlBuilder.cs` (3 new methods), `Diagnostics/InquiryDiagnosticDescriptors.cs` (INQ018/019/020).
- **MODIFY (every provider):** all 3 `*SqlBuilder.cs` (BuildPaginationClause; SqlServer also BuildKeysetPredicate).
- **MODIFY (`src/Inquiry`):** `Stores/InquirySelectAllAttribute.cs` + `InquirySelectAllByFieldAttribute.cs` (OrderBy/Paged).
- **ADD:** `Models/OrderingData.cs`, `Abstractions/SqlSelectOptions.cs`, `Stores/InquiryKeysetPageAttribute.cs`, `Paging/InquiryPage.cs`.

## 6. Cross-workstream dependencies & sequencing
- **W1:** keyset needs comparison predicates — but **build keyset's own `BuildKeysetPredicate`** (narrow, well-defined) so it doesn't block on W1; refactor to consume W1's comparison fragment later if desired.
- **SqlBuilder collision** with W1/W5/W8 — land the `SqlBuilder` contract additions + `SqlSelectOptions` as a small first PR all branch from (F3). ORDER BY (steps 1–2) is a clean S prerequisite that can merge first.
- **W5 projections** also wants ORDER BY/LIMIT — design `SqlSelectOptions` to take only quoted-column fragments + param names (not entity-coupled) so projections reuse it.
- Keep `StoreProcessor`/`StoreOperationEmitter` additions append-only.

## 7. Test strategy
Generator unit tests: exact const SQL for ORDER BY/offset/keyset across all 3 dialects (esp. SqlServer OFFSET/FETCH vs LIMIT; row-value vs OR-form keyset); diagnostics. Integration (SQLite primary, SqlServer/PG parity): ordered results; offset boundaries incl. last partial + empty page; keyset forward paging round-trip, null first page, HasMore at end, multi-column tie-break. Struct test for `InquiryPage`.

## 8. Risks / open questions
- SQL Server keyset + FETCH: verify index seek isn't defeated by `(@cursor IS NULL OR …)` (parameter sniffing) — consider two SQL variants (first vs next page) for seekability; benchmark before deciding.
- Multi-column cursor as value tuple — confirm `NextCursor` populated from last materialized entity's key props (no extra query).
- Offset param convention by position (consistent with existing positional matching), documented.
- `OrderBy` parse: restrict to `field [ASC|DESC]` v1; diagnose collation/`NULLS FIRST` etc.

## 9. Size: **L** — ORDER BY (S) + offset (M) + keyset (M–L: new return struct, multi-column predicates, per-dialect divergence), all touching SqlBuilder + 3 providers + new diagnostics + matrixed tests. ORDER BY can ship as an S first PR.
