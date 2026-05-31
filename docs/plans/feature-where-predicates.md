# W1 — Richer WHERE Predicates

> See [README.md](README.md). Depends on: **F2** (WHERE composition), **F3** (additive SqlBuilder). **Land first among the WHERE family** (W2/W6/W8 build on the predicate model + composition it establishes). Size: **L**. Contention: **HIGH** (SqlBuilder contract + all 3 providers + runtime for `IN`).

## 1. Feature summary & surface (chosen: enriched per-criterion attributes)
Today: equality-only `WHERE col = @param`. Extend to comparison (`> >= < <= <>`), `BETWEEN`, `IN (...)`, `LIKE`, `IS [NOT] NULL`, AND/OR. Must stay **compile-time** (no runtime LINQ/expression engine).
```csharp
[InquirySelectAllByPredicate]
[InquiryWhere("Price", Compare.GreaterThanOrEqual)]   // Price >= @Price (param 0)
[InquiryWhere("Name",  Compare.Like)]                 // AND Name LIKE @Name (param 1)
public partial Task<IReadOnlyList<Product>> SearchAsync(decimal price, string name, CancellationToken ct = default);

[InquirySelectAllByPredicate]
[InquiryWhere("CategoryId", Compare.In)]              // IN expansion (collection param)
public partial Task<IReadOnlyList<Product>> InCategoriesAsync(IReadOnlyList<int> categoryId, CancellationToken ct = default);
```
`[InquiryWhere]` (`AllowMultiple`) maps positionally to method params in declaration order. `IsNull`/`IsNotNull` consume 0 params; `Between` consumes 2 (`@x_lo`/`@x_hi`); `In` consumes 1 collection param. AND default; optional `Or`/`Group` for OR.

## 2. Approach options + recommendation
- **A (RECOMMENDED): enriched criteria attributes** + `[InquirySelectAllByPredicate]` marker. Reuses positional-binding machinery (`MatchesPositionalColumns`), field names validated via `FindColumn` (keeps INQ007), operators are a closed `enum` (no parser). Strongly typed.
- **B: filter DSL string** `[InquiryQuery("Price >= @min AND ...")]` — compact, natural AND/OR, but needs a mini-parser + new diagnostic family + by-name binding; biggest risk of becoming a runtime query engine. Defer; the structured model underneath is identical so B can layer on later.
- **C: runtime spec object** — violates const-SQL invariant. Rejected.

Ship **A**, AND-only first, then single optional OR-group level. Defer nested boolean trees (YAGNI).

## 3. Design
- **New shared model** `Abstractions/SqlPredicate.cs` (public): `enum SqlCompareOp {Equal,NotEqual,GreaterThan,…,Like,In,Between,IsNull,IsNotNull}`; `SqlPredicate(IColumn Column, SqlCompareOp Op, string? ParameterName, string? ParameterNameHi, bool IsOr)`; static `ParameterArity(op)`.
- **Public attributes** (`src/Inquiry/Stores/`): `Compare` enum, `InquiryWhereAttribute` (`AllowMultiple`, ctor `(field, op=Equal)`, optional `Or`), `InquirySelectAllByPredicateAttribute`.
- **Generator:** add `StoreOperation.SelectAllByPredicate`; `GetOperation`/`ExtractMethod` read all `[InquiryWhere]` in source order into a value-equatable `PredicateData(Field, Op, IsOr)` on `StoreMethodData`; `TryValidateForEmit` resolves fields via `FindColumn`, validates total param arity == non-CT params + per-op type (`IN`→collection of column type, INQ018; `Between`→2 same-typed; `Like`→string, INQ019). `Emit` emits one `const string _sqlPredicate_<suffix>` per distinct shape.
- **SqlBuilder contract (F3):** add `BuildSelectByPredicateSql(context, IReadOnlyList<SqlPredicate>)` with a `protected` base render helper (quoting, AND/OR join, param naming) + small `virtual` per-op hooks (`RenderLike` w/ optional ESCAPE, `RenderIn`). Comparison/Between/Null are dialect-uniform (base). All 3 providers compile against the new member but mostly inherit.
- **`IN` (the hard case):** SQL is `const`, but `IN` arity is runtime-variable. **Recommended:** emit a single-placeholder sentinel `col IN (@CategoryId)` in the const SQL; at bind time the generated binder detects the collection and rewrites command text to `@CategoryId0,…,@CategoryIdN` + adds N params. This needs a **runtime command-text-rewrite path** (route IN-bearing methods through the `InquiryCommand`/`DbCommandBinder` non-const path — an allocating path, acceptable since IN is inherently dynamic). Empty collection → `1=0` (no rows). Non-IN predicates keep the zero-alloc fast path.
- **Binding:** scalar ops reuse `EmitFastQueryListByFields`/`AppendBinderLambda` (already `@PropertyName` positional). `Between` → 2 params/column (binder maps predicate→param indices). Null ops → 0 params.

## 4. Implementation steps (TDD)
1. Shared model + enum + `StoreOperation.SelectAllByPredicate`. *Verify:* Generators.Tests compiles; `StoreMethodData` equality/caching unaffected.
2. Public attributes. *Verify:* attribute ctor/AllowMultiple tests.
3. SqlBuilder contract + base helper + per-op hooks; implement/inherit in all 3 providers. *Verify:* builder unit tests asserting WHERE string per op per dialect.
4. Generator discovery + validation (INQ007/018/019). *Verify:* `InquiryGeneratorTests` happy-path emits exact `_sqlPredicate_… = "...WHERE \"Price\" >= @Price AND \"Name\" LIKE @Name"`; negative diagnostics.
5. Emit scalar-op bodies. *Verify:* binder lambda + param names; SQLite integration over Products (`>=`, LIKE, BETWEEN, IS NULL).
6. `IN` expansion runtime path. *Verify:* placeholder SQL test; SQLite integration non-empty + empty; isolated expansion-helper unit test.
7. OR grouping (last). *Verify:* `… WHERE a = @a OR b = @b`.
8. Cross-dialect parity (SqlServer/PostgreSql integration, esp. IN + LIKE).

## 5. Shared-file contention map
- **MODIFY (shared):** `Abstractions/SqlBuilder.cs` (new method + base helper), `Models/StoreMethodData.cs`, `Models/StoreOperation.cs`, `StoreProcessor.cs` (GetOperation/ExtractMethod/TryValidateForEmit/MatchesPositionalColumns/Emit), `StoreOperationEmitter.cs` (Between/IN binder), `Infrastructure/GeneratorHelpers.cs` (read repeated attrs in order), `Diagnostics/InquiryDiagnosticDescriptors.cs` (INQ018/019).
- **MODIFY (every provider):** all 3 `*SqlBuilder.cs` (compile against new member; maybe LIKE/IN overrides).
- **MODIFY (runtime, IN only):** `IInquiry.cs`/`DefaultInquiry.cs`/`InquiryRequestPipeline.cs`(+Transacted) for command-text rewrite (or confine to the `InquiryCommand` non-const path), `Parameters/InquiryParameterBinder.cs` (collection expansion helper).
- **ADD:** `Abstractions/SqlPredicate.cs`, `Models/PredicateData.cs`, `Stores/Compare.cs`/`InquiryWhereAttribute.cs`/`InquirySelectAllByPredicateAttribute.cs`, integration + builder unit tests.

## 6. Cross-workstream dependencies
- Establishes the **predicate model + WHERE composition** that **W8 soft-delete**, **W6 concurrency**, **W2 keyset** all want. **Land W1 first**; make `SqlBuilder` additions additive (F3) so siblings rebase clean. All WHERE-shaping should funnel through one base render helper, not per-provider string concat.
- **W9 FTS** is conceptually a predicate kind — if W1 reshapes the WHERE surface, FTS may become an op under it; else FTS stays standalone.
- **W4 prepared statements:** naive `IN` expansion makes SQL non-constant → defeats preparation. Either exclude IN from prepare or use array params (Npgsql `= ANY(@ids)`) which keep SQL constant. Flag explicitly.

## 7. Test strategy
Generator unit tests (exact const SQL per op, binder lambdas, diagnostics). Builder unit tests (fastest cross-dialect net, no DB). Integration over Products per provider (SQLite always; SqlServer/PostgreSql gated): all ops + IN non-empty/empty + AND/OR. Regression: existing `SelectAllByField` equality path stays green (additive).

## 8. Risks / open questions
- **YAGNI boundary (central):** stop at flat AND + single OR-group. No nested trees / sub-selects / joins / computed expressions / runtime evaluator.
- **`IN` forces an allocating runtime path** — isolate it so non-IN stays zero-alloc; decide accept-rewrite vs fixed-arity-first.
- OR grouping in attributes only yields left-to-right (no parentheses) — is OR even in v1, or AND-only?
- LIKE escaping: values are parameters (injection-safe) but `%`/`_` are wildcards — document caller-escapes; per-dialect ESCAPE later.
- `Between` 2-param naming must disambiguate (suffix `_lo`/`_hi`) deterministically in SQL + binder.

## 9. Size: **L** — scalar ops alone are M (additive, base render helper); `IN` runtime rewrite + cross-cutting SqlBuilder contract across all providers + sibling coordination push to L.
