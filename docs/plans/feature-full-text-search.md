# W9 — Full-Text Search

> See [README.md](README.md). Depends on: **F3**; soft-dep on **W1** (FTS is a special predicate — if W1 reshapes the WHERE surface, FTS may become a predicate kind under it). Size: **M**. Contention: **MEDIUM** (SqlBuilder method + StoreProcessor/Emitter + new op).

## 1. Feature summary, scope & surface
New op `[InquiryFullTextSearch(...)]` on a store method taking a single `string` term (+ trailing `CancellationToken`), returning matching entities. Structurally a near-clone of `SelectAllByField`; novelty is per-dialect WHERE + the index prerequisite.
```csharp
[InquiryFullTextSearch("Title", "Body")]
public partial Task<IReadOnlyList<Article>> SearchAsync(string searchTerm, CancellationToken ct);
```
**Recommended scope (Tier-3, single-term, no ranking):** PostgreSQL + SQL Server (+ MySQL at the abstraction level once that provider exists). **SQLite FTS5 documented-only** (different schema model — virtual table, not the base table → diagnostic). **Index/schema DDL documented-only, not generated** (clean handoff to W7).
Per-engine WHERE (cols `Title,Body`, param `@searchTerm`):
- PostgreSQL: `WHERE to_tsvector('simple', coalesce("Title",'') || ' ' || coalesce("Body",'')) @@ plainto_tsquery('simple', @searchTerm)`
- SQL Server: `WHERE FREETEXT(([Title],[Body]), @searchTerm)` (natural-language, injection-safe; `CONTAINS` later via option)
- MySQL (future): `WHERE MATCH(\`Title\`,\`Body\`) AGAINST (@searchTerm IN NATURAL LANGUAGE MODE)`
- SQLite: unsupported → diagnostic.

## 2. Approach (recommended A)
Minimal, documented-only schema: one op + one attribute + one `BuildFullTextSearchSql` per dialect; index/catalog/`tsvector`/`FULLTEXT` setup is the user's responsibility (documented copy-paste DDL). Reject B (generate index DDL — overlaps W7, complex/stateful) and C (generic raw-predicate escape hatch — abandons the cross-dialect promise).

## 3. Design
- **Attribute:** `src/Inquiry/Stores/InquiryFullTextSearchAttribute.cs` (clone `InquirySelectAllByFieldAttribute`: `params string[] columns`). Same `Inquiry.Stores` namespace so `IsStoreAttribute` accepts it.
- **Op:** add `FullTextSearch` to `StoreOperation`. Reuse `StoreMethodData.FieldNames` for searched columns (no new model field).
- **Discovery (`StoreProcessor`):** `GetOperation` case; `ExtractMethod` reuses field-name extraction; `returnsList` includes it; `HasSupportedReturnType` shares the SelectAll arm (`IAsyncEnumerable<Entity>`/`Task<IReadOnlyList<Entity>>`).
- **Validation:** `TryValidateForEmit` resolves `FieldNames` to columns (reuse, INQ007); `HasSupportedParameters` requires exactly one non-CT `string` param (INQ006). New `INQ018 FullTextSearchNotSupportedByDialect`.
- **Const SQL:** `Emit` adds an `ftsOps` grouping → `_sqlFts_<suffix>` via `BuildFullTextSearchSql(ctx, columns)`.
- **Body (`StoreOperationEmitter`):** new `FullTextSearch` case; add a dedicated `EmitFtsQuery` helper binding ONE `@searchTerm` param (FTS has one search param, not one-per-column) — all dialects emit `ParameterName("searchTerm")` in the predicate so SQL + binder stay in lockstep. Reuse `QueryListAsync`/`QueryAsync` (overloads exist; no runtime change).
- **SqlBuilder:** add `abstract BuildFullTextSearchSql(ctx, searchColumns)` + `virtual bool SupportsFullTextSearch => true`. PG + SqlServer override the builder; SQLite overrides `SupportsFullTextSearch => false` and `StoreProcessor` reports INQ018 (diagnostic-driven, not throwing).
- **Schema prerequisite (documented):** per-engine index DDL — PG GIN expression index matching the emitted `to_tsvector` expression exactly; SqlServer `CREATE FULLTEXT CATALOG` + `CREATE FULLTEXT INDEX … KEY INDEX <pk>`; MySQL `ALTER TABLE … ADD FULLTEXT(...)`; SQLite FTS5 virtual-table explanation (out of scope).

## 4. Implementation steps (TDD)
1. Attribute + enum + discovery wiring. *Verify:* `[InquiryFullTextSearch("Title","Body")]` produces no INQ005/006 + a partial.
2. Emit-stage validation (single-string param arm; `SupportsFullTextSearch`/INQ018). *Verify:* INQ006 (bad sig), INQ007 (unmapped column), INQ018 (SQLite).
3. `BuildFullTextSearchSql` (PG + SqlServer) + `SupportsFullTextSearch`. *Verify (load-bearing):* exact `_sqlFts_<suffix>` per dialect (PG `to_tsvector … @@ plainto_tsquery`, SqlServer `FREETEXT(([Title],[Body]), @searchTerm)`).
4. Body emission (`EmitFtsQuery` binding `@searchTerm`). *Verify:* body calls `QueryListAsync<…,string,…>`/`QueryAsync`, binder `_p0.ParameterName = "@searchTerm"`.
5. Const-SQL grouping (`ftsOps`). *Verify:* shared vs distinct consts.
6. Integration (engine-gated): PG with GIN index; SqlServer with full-text catalog+index (CI-gate if FT service unavailable). SQLite: assert compile-time rejection.
7. Docs (per-engine index DDL + SQLite limitation).

## 5. Shared-file contention map
- **MODIFY (shared):** `Abstractions/SqlBuilder.cs` (abstract `BuildFullTextSearchSql` + virtual flag — coordinate w/ W7/W1), `Models/StoreOperation.cs`, `StoreProcessor.cs` (**highest-contention** — GetOperation/ExtractMethod/HasSupported*/TryValidateForEmit/Emit), `StoreOperationEmitter.cs` (case + `EmitFtsQuery`), `Diagnostics/InquiryDiagnosticDescriptors.cs` (INQ018), 3 `*SqlBuilder.cs`.
- **ADD:** `Stores/InquiryFullTextSearchAttribute.cs`, tests, docs.
- **NOT touched:** `SqlBuildContext.cs`, `IColumn.cs`, `StoreMethodData.cs` (reuses FieldNames), runtime.

## 6. Cross-workstream dependencies & sequencing
- **W1:** FTS is a special predicate. If W1 generalizes the WHERE-builder, FTS could become a predicate kind instead of a top-level op — **let W1 land first if it reshapes the WHERE surface**; else FTS standalone is the lower-risk independent path. Both share `StoreProcessor` + `SqlBuilder` → don't run both isolated simultaneously without a merge plan.
- **W7 migrations:** FTS index DDL belongs to W7; this defines the documented contract + leaves a `BuildFullTextIndexDdl` extension point. Don't generate DDL here.
- **E1/E2:** `BuildFullTextSearchSql` is `abstract` → **cannot merge until all 3 current providers compile** (and future MySQL/Oracle must implement it). Coordinate with in-flight provider workstreams; consider `virtual`-with-throw if abstract is too coupling.
- **Suggested:** W1 (if reshaping) → W9 → W7 FTS index DDL → MySQL/Oracle FTS overrides.

## 7. Test strategy
Generator emitted-SQL (primary): exact `_sqlFts_<suffix>` + body (binder `@searchTerm`, correct generic args) per dialect. Diagnostics (INQ006/007/018). Grouping. Integration (engine-gated): PG GIN index matching the emitted expression; SqlServer catalog+index (CI-gate). SQLite: compile-time rejection. Negative: multi-param FTS rejected.

## 8. Risks / open questions
- Per-dialect divergence is real (PG `@@`/tsvector, SqlServer CONTAINS/FREETEXT, MySQL MATCH/AGAINST share no syntax) — abstraction contains it, but correctness depends on user-created indexes the compiler can't verify.
- SqlServer `CONTAINS` needs valid predicate syntax (injection-adjacent) → **default `FREETEXT`** (safe natural language); `CONTAINS` later via option.
- PG index must match the emitted `to_tsvector` expression exactly (config + coalesce order) or the GIN index isn't used (silent perf cliff) — document exact DDL; expose regconfig as an arg later.
- SQLite FTS5's separate virtual-table model → out of scope + INQ018.
- Ranking/highlighting deferred (would need a score column → projection/ORDER BY change).
- `abstract` vs `virtual` for the builder method (merge-blocking vs safer).

## 9. Size: **M** — near-mechanical clone of `SelectAllByField`, but touches 5 shared generator files + 3 providers + new diagnostic + capability flag, and per-dialect SQL correctness + engine-gated integration (esp. SqlServer full-text setup) add real effort. No runtime/DDL changes.
