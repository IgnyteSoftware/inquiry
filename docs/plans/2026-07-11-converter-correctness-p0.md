# Converter correctness P0 group (#194 + #195)

## Goal and sequencing

Close two cross-provider correctness gaps as one reviewable group:

1. **#194 — reject nullable converter provider types.** Database nullability is owned by the model property; `TProvider` must be a supported non-null scalar.
2. **#195 — apply converters to `[InquiryDeleteAll]` key collections.** Every non-null model key must pass through its configured `ToProvider` exactly once before the active collection transport binds it.

Implement #194 first. It establishes that collection projection always receives a non-null `TProvider` contract and prevents #195 from needing to define a second provider-result-null policy.

Success means generator regressions pass for all six dialects, a live SQLite converter-backed DeleteAll round trip passes, non-converter DeleteAll output remains unchanged, and no per-element reflection/dynamic dispatch or captured closure is introduced.

## #194: reject and discard nullable `TProvider`

### Proven current defect

`EntityProcessor.ResolveConverter` builds `TypeData` from the converter's `TProvider`. `TypeData.Create` records `IsNullable` while classifying the non-null underlying primitive, but `IsSupportedConverterProviderType` does not reject `IsNullable`. Therefore `Guid?`, `bool?`, and annotated `string?` provider types are accepted.

Emission guards the **model property**, not the converter result. A non-null model whose `ToProvider` returns null can flow into an `(object)` cast as raw null instead of `DBNull.Value`, while DDL and materializer nullability remain model-driven. Supporting that safely would require a new public null contract and is out of scope for 1.0.

### Implementation

In `src/Inquiry.Generators.Shared/EntityProcessor.cs`:

- Make `IsSupportedConverterProviderType` return false when `type.IsNullable` is true, before testing the supported primitive kinds.
- After reporting existing error INQ038 in `ResolveConverter`, return `null` instead of retaining `ConverterData`.
- Apply the discard behavior to every unsupported provider type, not only nullable ones. INQ038 already makes the mapping invalid; retaining it only creates cascading generated-code/compiler failures.
- Keep `[InquiryJson]` unchanged. Its built-in provider is the non-null scalar `string`; nullable model JSON properties express database NULL through the model property guard.

Do not introduce a new diagnostic ID. Update the INQ038 description/documentation to say that `TProvider` must be a supported **non-null** scalar and that model-property nullability controls database NULL.

### Red tests

Extend `tests/Inquiry.Generators.Tests/ConverterGeneratorTests.cs` with distinct converters using:

- `Guid?`
- `bool?`
- annotated `string?`
- one already unsupported custom provider type

For each, assert:

- exactly one INQ038 at the mapped property;
- the diagnostic names the nullable/unsupported provider type;
- no generated materializer or store expression references the rejected converter;
- no secondary C# compiler diagnostic is caused by an invalid generated converter call.

Preserve and strengthen the valid case:

- model property `TModel?` with converter `IInquiryValueConverter<TModel, TProvider>` where `TProvider` is non-null;
- read path guards `IsDBNull` before `FromProvider`;
- write path tests model null before calling `ToProvider` and emits `DBNull.Value`;
- a non-null model calls `ToProvider` exactly once.

Cover nullable value-type and nullable reference-type model properties. Nullable **models** remain supported; nullable **provider types** do not.

## #195: one collection projection funnel

### Root cause

Predicate `IN`/`NOT IN` already routes converter-backed collections through `ProjectedCollectionExpression`. DeleteAll duplicates transport selection and passes the raw model collection directly to `InquiryJsonArrayParameter`, `InquiryArrayParameter`, `InquiryTvpParameter`, or `InquiryInExpansion`. A strongly typed model key therefore reaches a provider expecting its database scalar.

### Refactor shape

In `src/Inquiry.Generators.Shared/StoreOperationEmitter.cs`, generalize the existing predicate-only helpers around `ColumnData`:

```csharp
private static string CollectionBindingExpression(
    SqlBuilder sqlBuilder,
    ColumnData column,
    string sqlParameterName,
    string argumentExpression,
    bool isNegatedCollection)

private static string ProjectedCollectionExpression(
    ColumnData column,
    string argumentExpression)
```

The common binding helper owns, once:

- provider-value projection;
- `ResolveDbType` and size/precision arguments;
- negated expansion;
- native array/TVP/JSON transport selection;
- fallback per-element expansion.

Adapt predicate binding by passing `binding.Column`, `binding.SqlParameterName`, the method argument, and `binding.IsNegatedCollection`.

Adapt DeleteAll by passing the single key `ColumnData`, `sqlBuilder.ParameterName("keys")`, its collection argument, and `false`. Remove DeleteAll's duplicate DbType/size/transport construction.

This ensures hard-delete and soft-delete DeleteAll share the same projection because both use the same emitted binder; only `_sqlDeleteAll` differs.

Do not construct a fake `PredicateBinding` for DeleteAll. `ColumnData` and explicit transport inputs are the actual common abstraction.

### Projection semantics

For a converter-backed column, emit one null-guarded deferred `Enumerable.Select` with a `static` selector and the cached converter:

#### Non-nullable value-type model

```csharp
keys is null ? null : Enumerable.Select(
    keys,
    static value => InquiryConverterCache<TConverter>.Instance.ToProvider(value))
```

#### Nullable value-type model

```csharp
keys is null ? null : Enumerable.Select(
    keys,
    static value => value.HasValue
        ? (TProvider?)InquiryConverterCache<TConverter>.Instance.ToProvider(value.Value)
        : null)
```

`TProvider` is known non-null after #194. The nullable projection result represents a null **model element**, not a nullable converter provider contract.

#### Reference-type model

Guard every element regardless of nullable annotation because runtime collections can still contain null:

```csharp
keys is null ? null : Enumerable.Select(
    keys,
    static value => value is null
        ? (TProvider?)null
        : InquiryConverterCache<TConverter>.Instance.ToProvider(value))
```

For reference-type `TProvider`, use a nullable reference cast; for value-type `TProvider`, use `Nullable<TProvider>`. Generate this from `ConverterData.ProviderType.IsValueType`/display data rather than guessing from `SpecialType`.

Null semantics are explicit:

- a null collection remains the transport's existing empty collection/no-op;
- a null element never calls `ToProvider`;
- a null element becomes the transport's NULL representation and cannot match a non-null primary key;
- non-null elements call `ToProvider` exactly once;
- an empty collection remains a zero-row DeleteAll.

Plain columns and enum-as-string columns continue using the existing common behavior. Preserve enum-as-string projection when moving the helper.

### Transport matrix

The projected enumerable's element type must be `TProvider` (or nullable `TProvider` only for null model elements), allowing the existing transport to do its normal work:

| Dialect | DeleteAll transport | Required assertion |
|---|---|---|
| SQLite | `InquiryJsonArrayParameter` / `json_each` | JSON contains provider scalars, not model objects |
| SQL Server | `InquiryTvpParameter` | TVP resolves the provider type and receives provider values |
| PostgreSQL | `InquiryArrayParameter` | native typed array is a provider-type array |
| MySQL | `InquiryJsonArrayParameter` / `JSON_TABLE` | JSON_TABLE type comes from converter provider metadata |
| MariaDB | `InquiryJsonArrayParameter` / `JSON_TABLE` | same as MySQL |
| Oracle | `InquiryJsonArrayParameter` / `JSON_TABLE` | SQL conversion and JSON values use provider representation |

Fallback `InquiryInExpansion` must receive the provider DbType, declared metadata, and provider values. `NOT IN` predicate behavior remains unchanged but exercises the generalized helper.

## Performance contract

- Use the existing `InquiryConverterCache<TConverter>.Instance`; never instantiate a converter per element.
- Use a `static` selector. Generated code must contain no captured lambda/closure object.
- Exactly one `ToProvider` invocation per non-null source element.
- Null elements invoke it zero times.
- Keep projection deferred; do not pre-materialize an intermediate array/list in generated code.
- Permit the single iterator object already inherent in the existing predicate projection. Do not add another `Where`, `Select`, or enumeration pass.
- Generator tests pin exactly one lazy `Select` feeding each dialect transport. Live SQLite uses a counting enumerable/converter to prove one enumeration and one conversion per non-null element; the other runtime transports are unchanged and retain their own enumeration tests rather than gaining new provider-specific counters here.
- No reflection, `dynamic`, `Delegate.DynamicInvoke`, or per-element type inspection is added to generated code.
- Plain-key DeleteAll should remain allocation- and output-equivalent: it must not gain a projection.

## Generator test plan

Add focused cases to `tests/Inquiry.Generators.Tests/BatchDeleteGeneratorTests.cs` and/or a new `ConverterDeleteAllGeneratorTests.cs`.

Use a strongly typed key model converted to `decimal` or `string`, plus nullable value and reference model variants. Run each dialect explicitly:

- SQLite
- SQL Server
- PostgreSQL
- MySQL
- MariaDB
- Oracle

For every dialect assert:

- DeleteAll SQL uses the converter's provider column/collection type;
- generated binder contains a null-guarded `Enumerable.Select`;
- selector is `static`;
- selector calls cached `ToProvider` once syntactically;
- the selected enumerable is passed to the correct dialect transport;
- the raw model collection is not passed directly to that transport;
- provider metadata is used where the transport accepts metadata;
- empty/null collection behavior remains the established transport behavior.

Add separate snapshots for:

- nullable value-model elements (`HasValue`, `.Value`, null provider projection);
- reference-model elements (`is null`, no converter call on null);
- soft-delete DeleteAll;
- plain key and enum-as-string non-regressions;
- predicate IN/NOT IN output remaining behaviorally identical after helper generalization.

Add a generator/runtime unit test with an enumerable that counts enumeration and a test converter that counts `ToProvider`. Assert one enumeration and one conversion per non-null item, zero conversions for null elements. Test instrumentation may be stateful; production converters remain stateless by contract.

## Live SQLite proof

Add `tests/Inquiry.Sqlite.Tests/ConverterDeleteAllIntegrationTests.cs` with a small isolated entity/table/store:

- strongly typed ID model;
- converter to a SQLite-supported provider primitive (`long` or `string`);
- converter-backed single primary key;
- Insert/InsertAll, SelectByKey/SelectAll, and DeleteAll methods.

Tests:

1. Insert at least three rows using model IDs.
2. Delete a non-contiguous subset through `DeleteAllAsync(IEnumerable<TModel>)`.
3. Assert the affected-row count and that only the intended provider keys disappeared.
4. Pass an empty collection and assert zero rows affected.
5. Exercise a null collection if the public method shape permits it and confirm the established no-op.
6. Add a nullable value-model key fixture or focused variant with `[id1, null, id3]`; assert null does not invoke the converter and does not delete an unrelated row.
7. Add a reference-model variant or generator-level proof that a null element is not converted.
8. Use a test-only counter to prove `ToProvider` is called exactly once for each non-null key during DeleteAll (reset the counter after setup inserts).

SQLite is the required live proof because it is deterministic/in-process and its JSON transport would otherwise try to serialize the unsupported model object. Generator tests provide the six-dialect routing proof; the full provider suite remains the regression gate.

## Documentation

Update:

- `docs/site/articles/features/value-converters.md`
- `docs/site/articles/features/batch-operations.md`

Document:

- `TProvider` must be a supported non-null scalar; use nullable `TModel` to map database NULL;
- collection predicates and DeleteAll accept model values and project them through `ToProvider`;
- null collection elements never call the converter and cannot match/delete a non-null key;
- converters are cached and invoked once per non-null collection element.

## Unsigned converter-provider caveat — separate follow-up

Backlog and issue-body searches found no issue dedicated to unsigned/sbyte **converter provider collections**. Closed #48 and #49 cover materialization and scalar parameter storage, and closed #51 introduced converter projection for predicate collections, but none covers PostgreSQL's native `InquiryArrayParameter` returning `uint[]`/`ushort[]`/`ulong[]`/`sbyte[]` instead of Inquiry's signed storage partners.

The caveat already exists in predicate `IN` from #51; #195 merely exposes the same transport to DeleteAll. Do **not** expand this P0 group into runtime unsigned-array redesign. Track/deduplicate it separately against #48, #49, and #51, with scope covering all native collection transports and both predicate/DeleteAll callers.

## Validation

```powershell
dotnet test tests/Inquiry.Generators.Tests/Inquiry.Generators.Tests.csproj -c Release --no-restore
dotnet test tests/Inquiry.Tests/Inquiry.Tests.csproj -c Release --no-restore
dotnet test tests/Inquiry.Sqlite.Tests/Inquiry.Sqlite.Tests.csproj -c Release --no-restore

dotnet test tests/Inquiry.SqlServer.Tests/Inquiry.SqlServer.Tests.csproj -c Release --no-restore
dotnet test tests/Inquiry.PostgreSql.Tests/Inquiry.PostgreSql.Tests.csproj -c Release --no-restore
dotnet test tests/Inquiry.MySql.Tests/Inquiry.MySql.Tests.csproj -c Release --no-restore
dotnet test tests/Inquiry.MariaDb.Tests/Inquiry.MariaDb.Tests.csproj -c Release --no-restore
dotnet test tests/Inquiry.Oracle.Tests/Inquiry.Oracle.Tests.csproj -c Release --no-restore

dotnet build Inquiry.slnx -c Release --no-restore
dotnet test Inquiry.slnx -c Release --no-build
dotnet publish tests/Inquiry.AotSmoke/Inquiry.AotSmoke.csproj -c Release
git diff --check
```

Run the live provider projects on net8.0, net9.0, and net10.0 in the final group gate. Audit generated sources for non-static converter selectors, direct model collections passed to DeleteAll transports, duplicate `ToProvider` calls, and rejected nullable provider types retained downstream.
