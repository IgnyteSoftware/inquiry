# W5 — Projections (Partial Columns / DTOs) + Aggregations

> See [README.md](README.md). Depends on: **F5** (MaterializerEmitter extraction). Shares materializer path w/ **W10**. Size: **L**. Contention: **VERY HIGH** (`EntityProcessor` + new scalar pipeline path).

## 1. Feature summary & surface (recommended A)
Today every read hydrates the full entity; SQL always `SELECT <all> FROM t`. Add:
**Projections** — a subset result type:
```csharp
[InquiryProjection(typeof(Customer))]
public sealed record CustomerSummary {
    [InquiryColumn("CustomerID")] public string Id { get; init; }
    [InquiryColumn("CompanyName")] public string Name { get; init; }
}
[InquirySelectAll] public partial Task<IReadOnlyList<CustomerSummary>> ListSummariesAsync(CancellationToken ct = default);
```
**Scalar aggregates:**
```csharp
[InquiryCount] public partial Task<long> CountAsync(CancellationToken ct = default);
[InquiryCount(nameof(Customer.Country))] public partial Task<long> CountByCountryAsync(string country, CancellationToken ct = default);
[InquiryAggregate(InquiryAggregateFunction.Sum, nameof(Order.Freight))] public partial Task<decimal?> TotalFreightAsync(CancellationToken ct = default);
```
**Grouped aggregates (phase 2, gated)** — `[InquiryGroupKey]`/`[InquiryAggregateColumn]` into a projection.

## 2. Approach (recommended A)
Separate `[InquiryProjection(typeof(Entity))]` result type (a "subset entity with no key/relations/mutations") discovered as a first-class shape, reusing column-discovery + materializer emission. Aggregates = new scalar `StoreOperation`s. Reject column-list-into-tuple (B, fragile/unreadable) and runtime reflection projection (C, defeats the premise).

## 3. Design
- **Shared model:** `Models/ProjectionData.cs` (trimmed `EntityData`: name, namespace, entity FQN, `EquatableArray<ColumnData>`, materializer names). Add `Count`/`Aggregate`(/`GroupedAggregate`) to `StoreOperation`. `StoreMethodData` gains `string? ProjectionResultTypeFqn`, `AggregateKind`, `string? AggregateColumnName`. New `AggregateKind {None,Count,Sum,Avg,Min,Max}`.
- **F5 prerequisite:** extract `EntityProcessor`'s `EmitMaterializeBody`/`ReadExpression`/`ReadCallForSpecialType` into a shared `MaterializerEmitter` (behavior-preserving). Both `EntityProcessor` and a new `ProjectionProcessor` call it. **Land F5 as an isolated no-op refactor first** (existing snapshots byte-identical).
- **`ProjectionProcessor.cs`:** `Extract(symbol) → ProjectionData`; `EmitMaterializer(...)` resolves each projection column against the parent entity's `ColumnData` (deferred to emit stage, like `SelectAllByField` field resolution), emitting a struct+class materializer reading by **the projection's SELECT-list ordinal (0..n-1)**, NOT the entity's ordinals (the #1 correctness risk).
- **`InquiryGeneratorBase`:** add a 3rd `ForAttributeWithMetadataName` provider for `[InquiryProjection]`, `.Collect().Combine()`, emit projection materializers + register in `RegistrationEmitter` like entities.
- **`StoreProcessor`:** `ExtractMethod` resolves a select method's return element type to entity-or-projection (validated at emit against the projection set → diagnostic `ProjectionNotMapped`/`EntityMismatch`); `HasSupportedReturnType` accepts `Task<IReadOnlyList<TProjection>>`/`IAsyncEnumerable<TProjection>` and `Task<long>`/`Task<TScalar?>` for aggregates; `Emit` builds `SqlBuildContext` over the **projection's** columns (constructor already derives `SelectColumns` from whatever column list — so **no `SqlBuildContext` change needed**) or emits aggregate const SQL.
- **SqlBuilder (F3):** projection SELECT reuses `BuildSelectAllSql` (no new method). Add `virtual BuildCountSql(ctx, filterCols)` + `virtual BuildAggregateSql(ctx, fn, aggCol, filterCols)` — ANSI-portable, single base impl serves all 3 providers (near-zero per-provider edits).
- **Runtime scalar path (new — none exists):** add `ExecuteScalarAsync<T>(InquiryCommand, ct)` + allocation-free `ExecuteScalarAsync<T,TArgs>(string, TArgs, Action<DbCommand,TArgs>, ct)` to `IInquiryRequestPipeline` + both pipelines + `IInquiry`/`DefaultInquiry`, over `DbCommand.ExecuteScalarAsync()` with `DBNull`→default/null handling. Un-constrained generic (scalars are value types; existing read overloads require `class`). Additive via default-interface-methods.
- **New attributes:** `Entities/InquiryProjectionAttribute(Type)`, `Stores/InquiryCountAttribute(params string[])`, `Stores/InquiryAggregateAttribute(InquiryAggregateFunction, string col, params string[])` + `InquiryAggregateFunction` enum.

## 4. Implementation steps (TDD)
1. **Runtime scalar path first (independent).** Add `ExecuteScalarAsync`. *Verify:* `SELECT COUNT(*)` on SQLite returns `long`; mock confirms ambient-tx routing.
2. Aggregate attributes + enum. *Verify:* compiles + attribute tests.
3. SqlBuilder `BuildCountSql`/`BuildAggregateSql` (virtual base). *Verify:* emitted SQL per dialect.
4. Count/Aggregate ops (StoreOperation/StoreMethodData/GetOperation/HasSupported*/Emit/Emitter scalar body). *Verify:* snapshot + SQLite count/sum with/without filter.
5. **F5 extract `MaterializerEmitter`** (pure move). *Verify:* existing entity-materializer snapshots byte-identical.
6. Projection discovery + materializer + wire into generator + registration. *Verify:* `[InquiryProjection]` record emits struct+class materializer reading subset by ordinal; diagnostics (unknown/dup column, entity mismatch).
7. Projection-returning store methods (relax HasSupportedReturnType/ExtractMethod/Emit; build ctx over projection columns). *Verify:* snapshot `Task<IReadOnlyList<CustomerSummary>>`; SQLite behavioral (subset values; unselected columns not read).
8. **Phase 2 (gated):** grouped aggregates (`[InquiryGroupKey]`/`[InquiryAggregateColumn]`, `BuildGroupedAggregateSql`, `GroupedAggregate` op). *Verify:* `SUM … GROUP BY` into projection.

## 5. Shared-file contention map
- **MODIFY (highest):** `EntityProcessor.cs` (F5 refactor — coordinate with W10), `StoreProcessor.cs`, `StoreOperationEmitter.cs`, `Models/StoreOperation.cs` + `StoreMethodData.cs`, `InquiryGeneratorBase.cs` (new provider + emit loop), `RegistrationEmitter.cs`, `Abstractions/SqlBuilder.cs` (count/aggregate virtuals), `Diagnostics/InquiryDiagnosticDescriptors.cs`, `IInquiry.cs`/`DefaultInquiry.cs`/`Pipeline/IInquiryRequestPipeline.cs` + both pipelines (scalar overloads).
- **MODIFY (providers):** 3 `*SqlBuilder.cs` only if a provider overrides aggregate SQL (target: zero, ANSI base impl).
- **ADD:** `MaterializerEmitter.cs`, `ProjectionProcessor.cs`, `Models/ProjectionData.cs`, `AggregateKind.cs`, `Entities/InquiryProjectionAttribute.cs`, `Stores/InquiryCountAttribute.cs` + `InquiryAggregateAttribute.cs` + `InquiryAggregateFunction.cs`.
- **`SqlBuildContext.cs`:** likely NO change (derives SelectColumns from given list); only touch for grouped aggregates.

## 6. Cross-workstream dependencies
- **F5 / W10:** both edit materializer generation → land `MaterializerEmitter` extraction first (isolated), then projections + JSON-converters build on it.
- **SqlBuilder / W1 / W2:** projections want to compose with WHERE + ORDER BY/LIMIT — design `SqlSelectOptions` (W2) to take quoted-column fragments so projections reuse; coordinate `SqlBuilder` method-set additions. Grouped aggregates need WHERE/HAVING → phase 2 should depend on W1.
- **Pipeline:** `ExecuteScalarAsync` overlaps W2/W3 pipeline additions — DIM bridging lets them be added independently.

## 7. Test strategy
Generator snapshots: projection→struct+class materializer by ordinal; projection-returning methods (`IReadOnlyList`/`IAsyncEnumerable`); count/aggregate const SQL per dialect. Diagnostics: projection on non-entity / unknown column / aggregate on non-numeric / wrong return type. SQLite behavioral: subset values, count w/wo filter, SUM/AVG/MIN/MAX incl. NULL-result, aggregates in ambient tx. Incrementality: unrelated edit doesn't re-run projection emission. Regression: F5 leaves entity snapshots byte-identical.

## 8. Risks / open questions (YAGNI boundary)
- Projection column resolution deferred to emit (discovery can't see parent entity) — degrade with diagnostic, not crash.
- **Ordinal correctness** — read by projection SELECT-list ordinal, not entity index (#1 bug risk).
- Scalar conversion: SQLite returns `long` for COUNT; SUM may return double/long — robust `Convert.ChangeType`/`DBNull`/nullable-T.
- **In scope:** static projections; scalar COUNT/SUM/AVG/MIN/MAX; optional positional equality filter. **Phase 2:** single-level GROUP BY + 1 key + 1 aggregate + HAVING-on-aggregate. **Out:** arbitrary expression projections, computed columns, joins, runtime-composed predicates, DISTINCT, window functions.
- Projections are flat/read-only — no eager relations.

## 9. Size: **L** — phase 1 spans a new discovery provider + processor + the `EntityProcessor` materializer refactor + a brand-new scalar pipeline path through `IInquiry` + both pipelines. Phase 2 (grouped) is +M.
