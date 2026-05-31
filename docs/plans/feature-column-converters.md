# W10 — Advanced Column Types (Value Converters, JSON, Arrays, Enums)

> See [README.md](README.md). Depends on: **F5** (MaterializerEmitter), **F1** (ColumnData). Shares materializer path w/ **W5**. Size: **L**. Contention: **HIGH** (`EntityProcessor` materializer + `StoreOperationEmitter` binding + ColumnData).

## Grounding
Type mapping lives in **two** codegen sites: read (`EntityProcessor.ReadExpression`/`ReadCallForSpecialType` → `reader.GetXxx`) and write (`StoreOperationEmitter.BuildParameterValueExpression` → `_p.Value`). **Enums already work as int** (read via underlying type + cast, bound coerced to int); the gap is enum-as-string, JSON, arrays. No converter infra exists. No DDL exists → per-dialect "column type" matters here only as **parameter typing hints** (e.g. Npgsql jsonb), not CREATE TABLE.

## 1. Feature summary & surface (recommended B)
Map a non-primitive CLR type to a column via a **compile-time-resolvable converter** transforming `T` ↔ `TProvider` (a primitive the existing paths handle).
```csharp
[InquiryColumn(Converter = typeof(MoneyToDecimalConverter))] public Money Price { get; set; }
[InquiryColumn, InquiryEnumAsString] public OrderStatus Status { get; set; }   // enum-as-int already works w/o attr
[InquiryColumn("metadata"), InquiryJson] public Dictionary<string,string> Metadata { get; set; }
[InquiryColumn("tags")] public string[] Tags { get; set; }                      // PG native array
```
Converter contract:
```csharp
public interface IInquiryValueConverter<TModel, TProvider> { TProvider ToProvider(TModel m); TModel FromProvider(TProvider p); }
```

## 2. Approach (recommended B)
General `IInquiryValueConverter<,>` as the core primitive; `[InquiryJson]`/`[InquiryEnumAsString]` are built-in converters/shorthands; PG arrays are mostly a read-path relaxation. Reject A (special-cased only — no extensibility) and C (expression-tree converters — not emittable by a generator, defeats zero-reflection/AOT).

## 3. Design
- **Public contract (`src/Inquiry`):** `Entities/IInquiryValueConverter.cs` (converters stateless, parameterless ctor; generator emits `new TConverter()` into a `static readonly` field, reused — no per-row alloc). `Entities/InquiryJsonAttribute.cs`, `InquiryEnumAsStringAttribute.cs`. Add `Type? Converter` to `InquiryColumnAttribute` (additive). Built-ins: `InquiryJsonConverter<T>` (System.Text.Json), `InquiryEnumAsStringConverter<TEnum>`.
- **Shared model:** `Models/ConverterData.cs` (value-equatable, symbol-free: `ConverterTypeDisplay`, `ProviderTypeDisplay`, `ProviderSpecialType`, `ProviderIsValueType`, `ConverterKind {None,Custom,Json,EnumAsString}`). Add nullable `ConverterData? Converter` to `ColumnData` (after **F1** init-only; one construction site).
- **Discovery (`EntityProcessor.DiscoverColumns`):** resolve converter — explicit `Converter=typeof(X)` (inspect `IInquiryValueConverter<,>` base for `TProvider`; diagnostic if not implemented / unsupported provider type) → `Custom`; else `[InquiryEnumAsString]` on enum → `EnumAsString`(string); else `[InquiryJson]` → `Json`(string); else null (unchanged). New `GeneratorHelpers.GetNamedType`.
- **Read (`MaterializerEmitter` after F5):** compute the provider-level read via existing `ReadCallForSpecialType` against `Converter?.ProviderSpecialType ?? type.SpecialType`, then wrap: Custom `<field>.FromProvider(read)`; EnumAsString `Enum.Parse<TEnum>(stringRead)`; Json `JsonSerializer.Deserialize<T>(stringRead)`. Existing `IsDBNull` branch wraps the whole expr (null → null/default, converter not called). Converter instance = `static readonly` field (emit once; share across struct+class materializer via a holder).
- **Write (`StoreOperationEmitter.BuildParameterValueExpression`):** branch if `column.Converter != null` → `(object?)<field>.ToProvider(<accessor>) ?? DBNull.Value` (null guards like the enum path). Converter field emitted as `static readonly` on the generated store (where the binder lambda lives). Also handle the secondary bind paths (`AppendPositionalParameters`, `EmitStoredProcedure`) or document them as not supporting converter columns.
- **Per-dialect typing:** JSON provider value is `string` — SqlServer/SQLite/MySQL accept a plain string param (no special handling); **PostgreSQL jsonb** needs the param typed jsonb (else "column is jsonb but expression is text"). Each provider runs its OWN generator, so the **PG emitter** can emit a `::jsonb` cast / `NpgsqlDbType.Jsonb` hint while others don't — confine the coupling to the PG analyzer (via a `ConverterData.RequiresJsonbHint` flag or a `virtual SqlBuilder.JsonParameterCast`). **PG arrays:** Npgsql maps `int[]`/`string[]` natively; blocker is the read path forcing `GetInt32` — route array CLR types to the `GetFieldValue<T[]>` fallback (classify arrays as non-primitive in `TypeData`). Non-PG arrays → diagnostic or require `[InquiryJson]`.

## 4. Implementation steps (TDD)
1. **Enum-as-string (smallest win).** `ConverterData`, `InquiryEnumAsStringAttribute`, discovery branch, read/write emit. *Verify:* generator test (`Enum.Parse`/`.ToString()`); SQLite round-trip.
2. **General `IInquiryValueConverter<,>` + custom converter** (+ `Converter=` arg + `GetNamedType`). *Verify:* `Money↔decimal` round-trips through generator + SQLite. Refactor enum-as-string to a built-in converter.
3. **JSON columns** (`InquiryJsonAttribute` + `InquiryJsonConverter<T>`). *Verify:* generator asserts `JsonSerializer` calls; SQLite text round-trip; SqlServer/MySQL nvarchar/JSON.
4. **PostgreSQL jsonb typing** (spike: cast vs `NpgsqlDbType`; confine to PG emitter). *Verify:* PG jsonb round-trip.
5. **PostgreSQL native arrays** (`TypeData` array classification; `GetFieldValue<T[]>` read; array bind). *Verify:* PG `int[]`/`string[]` round-trip; diagnostic for arrays on non-PG.
6. Diagnostics + docs (converter doesn't implement interface; unsupported provider type; array on wrong dialect; converter+relation conflict).

## 5. Shared-file contention map
- **MODIFY (high):** `Models/ColumnData.cs` (**shared w/ W5/W6/W7** — land F1 first), `EntityProcessor.cs`/`MaterializerEmitter` (read expr — **shared w/ W5**, extract via F5), `StoreOperationEmitter.cs` (binding), `Models/TypeData.cs` (array classification), `Infrastructure/GeneratorHelpers.cs` (GetNamedType), `Diagnostics/InquiryDiagnosticDescriptors.cs`, `Entities/InquiryColumnAttribute.cs` (Converter prop), PG only: `PostgreSqlSqlBuilder.cs`/`InquiryPostgreSqlGenerator.cs` (+ `SqlBuilder.cs` if adding a virtual hook). Avoid touching `Parameters/InquiryParameterBinder.cs` (prefer codegen at emit site).
- **ADD:** `IInquiryValueConverter.cs`, `InquiryJsonAttribute.cs`/`InquiryEnumAsStringAttribute.cs`, `Converters/InquiryJsonConverter.cs`/`InquiryEnumAsStringConverter.cs`, `Models/ConverterData.cs`, test fixtures.

## 6. Cross-workstream dependencies & sequencing
- **ColumnData hot collision** (W5/W6/W7 also extend it) — **land F1 (init-only/named-optional) first**, then all branch off; additive members merge clean.
- **Materializer overlap w/ W5** — both edit `EntityProcessor.EmitMaterializeBody`/`ReadExpression`. **Extract `ReadExpression` into the shared `MaterializerEmitter` (F5) before W5 forks it.**
- **W7 DDL** needs column SQL type, which this surfaces partially (JSON→jsonb hint, arrays) — share ONE "provider type descriptor", don't invent two.
- Relation property must not also be a converter column (diagnostic).

## 7. Test strategy
Generator unit tests (emitted read/write expr per converter kind; nullable converter guards `IsDBNull`/`HasValue`; diagnostics; incremental-cache stability — `ConverterData` is a record). SQLite round-trips (enum-as-string, custom, JSON-as-text). PG integration (jsonb, native `int[]`/`text[]`). SqlServer/MySQL JSON-as-nvarchar/JSON. Benchmark guard: converter columns don't regress the allocation-free binder.

## 8. Risks / open questions
- Compile-time converter expressibility — plain `new T()` types, not lambdas/expressions. Config (e.g. `JsonSerializerOptions`) baked into the converter type. Reject Expression-based (C).
- **AOT-friendliness of System.Text.Json** — reflection-based `Deserialize<T>` is not trim/AOT-safe (IL2026/IL3050), the biggest tension with inquiry's zero-reflection ethos. Document JSON as not-AOT-clean v1, or offer a `JsonTypeInfo`/`JsonSerializerContext` overload later.
- PG jsonb param typing couples generated code to a dialect — confine to the PG analyzer (feasible: each provider runs its own generator); prefer a SQL `::jsonb` cast or `DbType` hint over casting to `NpgsqlParameter` in the lambda.
- Arrays: PG-only native v1; other dialects → `[InquiryJson]` or diagnostic. Spatial out of scope v1.
- Secondary bind paths (`AppendPositionalParameters`/`EmitStoredProcedure`/eager loaders) must honor converters or be documented unsupported — easy to miss.
- ColumnData positional-record churn → resolve shape first (F1).

## 9. Size: **L** — enum-as-string + general converter + JSON-as-text is M; PG jsonb typing + native arrays (dialect coupling, cross-provider integration, AOT/JSON concerns) + ColumnData coordination push to L. Ship incrementally: M first cut (steps 1–3 via text/int), then PG jsonb/arrays (steps 4–5).
