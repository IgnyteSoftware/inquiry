# Unsigned native collection transports (#197)

## Goal

Make PostgreSQL native arrays and SQL Server TVPs use Inquiry's established signed storage partners for collection elements while preserving every unsigned/sbyte bit pattern:

| Logical/provider type | Native collection storage type | SQL storage |
|---|---|---|
| `sbyte` | SQL Server `byte`; PostgreSQL `short` carrying `0..255` | SQL Server `TINYINT`; PostgreSQL `smallint` (Npgsql reserves `byte[]` for scalar `bytea`) |
| `ushort` | `short` | `SMALLINT` / `smallint` |
| `uint` | `int` | `INT` / `integer` |
| `ulong` | `long` | `BIGINT` / `bigint` |

The conversion is an unchecked same-width reinterpretation, matching scalar bind/materialize behavior. It must cover direct, nullable-model, enum, and converter-provider collections used by positive `IN`, `NOT IN`, and DeleteAll.

## Foundation already merged

#199 now provides:

- compile-time collection-artifact discovery;
- stable schema-qualified SQL Server TVP descriptors and setup DDL;
- explicit generated TVP type names;
- I/O-free `InquiryTvpParameter.Bind`;
- `BindUnsupported` only when no descriptor exists.

#195 now provides a shared collection projection funnel for predicate collections and DeleteAll, including nullable value/reference model guards and exactly-once converter invocation.

#197 extends those seams. It must not reintroduce runtime artifact discovery, change artifact lifecycle, or duplicate DeleteAll/predicate transport logic.

## Root cause

Scalar paths already reinterpret unsigned/sbyte values before provider binding. Native collection transports do not consistently inherit that behavior:

- PostgreSQL `InquiryArrayParameter` can receive `uint[]`, `ulong[]`, etc.; Npgsql has no matching PostgreSQL unsigned array types.
- SQL Server's artifact descriptor is already based on signed `DbTypeClass`, but generated `Bind<T>` can still infer `T = uint/ulong/...`; `ResolveTypeInfo(typeof(T))` then disagrees with the generated artifact or selects `BindUnsupported` for unsupported signatures.
- unsigned-backed enums must be unwrapped to their underlying value before the storage-partner reinterpretation;
- converter collections must call `ToProvider` once and reinterpret that returned provider value, not the model value.
- empty and all-null collections contain no runtime sample from which a provider can infer a corrected element type, so normalization must be statically typed.

## Compile-time collection storage hook

Add provider-overridable compile-time context/result records beside the existing parameter/reader contexts:

```csharp
public readonly record struct CollectionElementExpressionContext(
    string ValueExpression,
    string ProviderTypeName,
    SpecialType ProviderSpecialType);

public readonly record struct CollectionElementExpression(
    string ValueExpression,
    string StorageTypeName,
    bool IsTransformed);
```

Add to `SqlBuilder`:

```csharp
public virtual CollectionElementExpression BuildCollectionElementExpression(
    CollectionElementExpressionContext context)
    => new(context.ValueExpression, context.ProviderTypeName, false);
```

PostgreSQL and SQL Server share the unsigned mappings but intentionally differ for `sbyte`:

```csharp
SQL Server System_SByte   => unchecked byte
PostgreSQL System_SByte   => unchecked short after reinterpretation through byte
SpecialType.System_UInt16 => unchecked short
SpecialType.System_UInt32 => unchecked int
SpecialType.System_UInt64 => unchecked long
```

The emitted expressions are direct C# casts, for example:

```csharp
unchecked((int)(valueExpression))
```

and the result reports the corresponding fully-qualified storage type (`global::System.Int32`). Other dialects inherit identity, preserving generated source and runtime transport behavior.

Do not use a dialect-name string branch in `StoreOperationEmitter`; provider behavior belongs in the builder override.

## One projection and exact ordering

Refactor `ProjectedCollectionExpression` so it constructs the element expression in this exact order:

1. Guard a null model element.
2. Unwrap nullable model `.Value` when present.
3. Call cached converter `ToProvider` when configured.
4. For a direct enum, use its underlying integral semantics.
5. Apply enum-as-string when configured (not an unsigned path).
6. Pass the effective provider expression/type to `BuildCollectionElementExpression`.
7. Wrap the transformed storage result in nullable storage only for a nullable model element.

Examples:

### Direct uint

```csharp
values is null ? null : Enumerable.Select(
    values,
    static value => unchecked((int)value))
```

### Nullable uint model element

```csharp
values is null ? null : Enumerable.Select(
    values,
    static value => value.HasValue
        ? (int?)unchecked((int)value.Value)
        : null)
```

### Converter model → uint

```csharp
values is null ? null : Enumerable.Select(
    values,
    static value => unchecked((int)
        InquiryConverterCache<MyConverter>.Instance.ToProvider(value)))
```

### Nullable/reference converter model → uint

The null branch returns `int?`; the non-null branch calls `ToProvider` once and reinterprets once.

### Enum : uint

```csharp
static value => unchecked((int)value)
```

The compile-time enum cast performs unwrap + storage reinterpretation in one expression. Do not call `Convert.ChangeType`, `Enum.GetUnderlyingType`, reflection, or dynamic conversion on the generated unsigned-enum path.

If neither converter/enum-as-string nor provider storage transformation is needed, return the original collection expression exactly. Plain signed collections on PostgreSQL/SQL Server and all plain collections on other dialects must not gain a LINQ iterator.

Use a single `Enumerable.Select` with a `static` selector when projection is required. Do not emit `Select(...).Select(...)`, `Where`, pre-materialization, or a captured closure.

## Empty and all-null collections

The transformed enumerable's compile-time element type is authoritative:

- empty `IEnumerable<uint>` becomes an empty `IEnumerable<int>` and ultimately an `int[]`;
- nullable/all-null `IEnumerable<uint?>` becomes `IEnumerable<int?>` and ultimately `int?[]`;
- converter-backed nullable/all-null collections likewise expose nullable signed provider storage.

PostgreSQL must therefore bind a typed signed/nullable-signed array even with zero non-null values. It must never inspect the first element to choose an array type.

SQL Server TVP binding already has an explicit generated `TypeName`; empty/all-null inputs must still use the matching signed artifact descriptor and `SqlMetaData`.

Null elements call no converter and match/delete no non-null database value. Preserve existing null/empty SQL semantics.

## SQL Server artifact and binder alignment

### Descriptor mapping

Do not create new unsigned TVP type categories or artifact names. The merged #199 `DbTypeClass` mapping already points to the correct signed artifact signatures:

- sbyte → `DbTypeClass.Byte` → `tinyint`
- ushort → `DbTypeClass.Int16` → `smallint`
- uint → `DbTypeClass.Int32` → `int`
- ulong → `DbTypeClass.Int64` → `bigint`
- unsigned-backed enums use the same mapping through their underlying type;
- converter-backed columns use their provider type's mapped class.

Add explicit tests so this is contractual rather than incidental. Direct, nullable, converter, and enum usages sharing the same schema/signed signature must deduplicate to the same artifact identity/type name as the corresponding signed storage type.

The descriptor's `ElementSignature` and emitted `CREATE TYPE ... [Value]` SQL remain signed. #197 should not version/change the #199 hash for a signature whose SQL storage is unchanged.

### `BindUnsupported` decision

After normalization, all four unsigned/sbyte categories and their enum/converter forms must resolve a #199 artifact and emit `InquiryTvpParameter.Bind`, never `BindUnsupported`.

`BindUnsupported` remains only for genuinely unsupported collection categories owned by later metadata work. Do not broaden it or add runtime fallback DDL.

### Runtime defensive mapping

Harden `InquiryTvpParameter.ResolveTypeInfo`/storage resolution so direct callers using an unsigned generic type select the same signed `SqlMetaData`:

- `sbyte` → `SqlDbType.TinyInt`
- `ushort` → `SqlDbType.SmallInt`
- `uint` → `SqlDbType.Int`
- `ulong` → `SqlDbType.BigInt`

Keep value handling order:

1. skip null;
2. if a runtime enum reaches the compatibility path, unwrap to its declared underlying type;
3. reinterpret unsigned values to short/int/long and sbyte to the provider-specific numeric carrier with `unchecked`;
4. call `SqlDataRecord.SetValue` with the signed partner.

Generated unsigned enums should already arrive as signed values and avoid runtime enum conversion. The defensive runtime path must agree with the descriptor but must not perform DDL, connection I/O, or artifact lookup.

For empty/all-null direct runtime inputs, metadata selection uses closed generic `T`, never the first element.

## PostgreSQL array handling

Generated PostgreSQL binders pass signed-partner enumerables into `InquiryArrayParameter.Bind`. `ToArrayValue` must produce:

- `short[]` for sbyte, containing its reinterpreted byte value widened to `0..255` so Npgsql binds `smallint[]` rather than `bytea`;
- `short[]` for ushort;
- `int[]` for uint;
- `long[]` for ulong;
- nullable equivalents when null elements are supported;
- signed arrays for unsigned-backed enums.

Because generated projection supplies the signed generic element type, the ordinary non-enum `ToArray()` path should be sufficient and should retain typed empty/all-null arrays.

Add defensive direct-helper normalization only if unit tests prove a supported public/EditorBrowsable invocation can still reach `InquiryArrayParameter` with unsigned `T`. Any fallback must use closed-generic/static typed loops, not `Array.CreateInstance`, reflection invocation, `dynamic`, or per-element `Convert.ChangeType`. Generated code remains the primary correctness boundary.

Signed enums and existing supported arrays must retain their current behavior.

## `IN`, `NOT IN`, and DeleteAll coverage

### Positive `IN`

PostgreSQL uses a native signed array; SQL Server uses the signed TVP artifact. Cover select predicates and set-based mutation predicates.

### `NOT IN`

Both providers currently use `InquiryInExpansion.ExpandNotIn`. It already unwraps enums and reinterprets unsigned/sbyte values. Keep that transport, but add boundary tests proving:

- converter projection occurs first;
- expansion receives provider values;
- unsigned reinterpretation occurs once after conversion;
- empty collection semantics remain correct.

Do not route `NOT IN` through TVP/ANY in this issue.

### DeleteAll

DeleteAll shares the #195 collection funnel. Cover:

- direct unsigned key;
- nullable unsigned model key/element where the supported store signature permits it;
- converter-backed model key → unsigned provider;
- unsigned-backed enum key;
- null and empty collections;
- soft-delete and hard-delete SQL paths at generator level.

## Other dialect stability

SQLite, MySQL, MariaDB, and Oracle retain their JSON/expansion behavior, which already reinterprets unsigned values according to the scalar storage contract.

Their `SqlBuilder` hook is identity. Assert:

- direct unsigned collections do not gain a generated `Select`;
- converter collections retain the #195 single projection text;
- JSON serialization/output and SQL text remain unchanged;
- no signed-partner array/TVP artifact logic appears;
- existing unsigned integration suites remain green.

Use exact generated-source comparisons where practical, not only broad `Contains` assertions.

## Exact files

Shared generator/API:

- add `src/Inquiry.Generators.Shared/Abstractions/CollectionElementExpressionContext.cs` (or equivalent context/result definitions)
- `src/Inquiry.Generators.Shared/Abstractions/SqlBuilder.cs`
- `src/Inquiry.Generators.Shared/StoreOperationEmitter.cs`

Provider analyzers/runtime:

- `src/Inquiry.PostgreSql.Analyzer/PostgreSqlSqlBuilder.cs`
- `src/Inquiry.SqlServer.Analyzer/SqlServerSqlBuilder.cs`
- `src/Inquiry/Parameters/InquiryArrayParameter.cs` only for proven defensive/direct-helper gaps
- `src/Inquiry.SqlServer/Parameters/InquiryTvpParameter.cs`

Feature/live fixtures and tests:

- add focused generator tests such as `tests/Inquiry.Generators.Tests/UnsignedCollectionGeneratorTests.cs`
- extend `tests/Inquiry.Generators.Tests/TvpArtifactGeneratorTests.cs` / `TvpGeneratorTests.cs`
- extend runtime tests for `InquiryArrayParameter` and `InquiryTvpParameter`
- add or extend a shared feature-catalog unsigned collection entity/store
- add `tests/Inquiry.PostgreSql.Tests/UnsignedCollectionIntegrationTests.cs`
- add `tests/Inquiry.SqlServer.Tests/UnsignedCollectionIntegrationTests.cs`
- update provider/value-converter documentation only where collection storage behavior is described

## Red generator matrix

For PostgreSQL and SQL Server, generate direct, nullable, converter-backed, and enum-backed collections for all four categories. Include positive IN, NOT IN, DeleteAll, and one predicate mutation.

Assert:

- SQL Server sbyte selector returns byte; PostgreSQL sbyte selector returns short after byte reinterpretation;
- ushort selector returns short;
- uint selector returns int;
- ulong selector returns long;
- casts are `unchecked`;
- converter `ToProvider` appears exactly once in the single static selector;
- unsigned enum is cast before/while producing the signed partner and no runtime `Convert.ChangeType` expression is generated;
- nullable selectors return nullable signed storage and do not convert null;
- null collection guard remains;
- PostgreSQL binds through `InquiryArrayParameter.Bind` with the projected enumerable;
- SQL Server binds through `InquiryTvpParameter.Bind` with the expected #199 signed artifact name;
- SQL Server never emits `BindUnsupported` for these cases;
- direct/signed/artifact-equivalent usages deduplicate correctly;
- empty/all-null source shapes compile with statically typed signed storage.

For SQLite, MySQL, MariaDB, and Oracle, assert exact source stability and absence of native signed projections.

Compile generated consumers with `CheckForOverflowUnderflow=true` to prove every boundary reinterpretation remains explicitly unchecked.

## Runtime unit tests

### PostgreSQL array helper

Verify the object assigned to the parameter is exactly:

- PostgreSQL `short[]`, `short[]`, `int[]`, or `long[]` for sbyte/ushort/uint/ulong respectively (SQL Server TVP rows use byte/short/int/long);
- the correct signed array for unsigned-backed enums;
- a zero-length typed signed array for empty input;
- a nullable signed array with preserved null slots for all-null/mixed nullable input.

Assert bit patterns at signed boundaries, for example `uint.MaxValue → -1` and `3_000_000_000u → -1_294_967_296`.

### SQL Server TVP helper

Verify:

- descriptor TypeName is the existing signed artifact;
- `SqlMetaData.SqlDbType` matches TinyInt/SmallInt/Int/BigInt;
- rows contain byte/short/int/long values;
- empty/all-null input still binds one Structured parameter with the correct TypeName;
- null elements do not create records;
- unsigned-backed enum compatibility unwrap happens before reinterpretation;
- `BindUnsupported` is not called;
- no connection/DDL/artifact I/O occurs.

## Exactly-once and allocation constraints

- One static `Enumerable.Select` only when conversion/storage normalization is required.
- No captured closure.
- `ToProvider` exactly once per non-null model element and zero times per null element.
- One unchecked reinterpretation per non-null unsigned provider value.
- One enumeration by the selected runtime transport.
- No reflection, `dynamic`, `Delegate.DynamicInvoke`, `Array.CreateInstance`, or per-element type discovery.
- No additional intermediate list/array before the transport's existing required materialization.
- Empty/all-null correctness must not scan twice or sample elements.

Use a test-only counting enumerable and converter to prove enumeration/conversion counts for IN and DeleteAll.

## Live PostgreSQL and SQL Server tests

Run every live scenario on net8.0, net9.0, and net10.0.

Create a focused schema whose physical columns use signed storage and whose CLR surface includes:

- direct sbyte/ushort/uint/ulong columns;
- nullable unsigned columns where valid;
- converter model → uint and model → ulong;
- unsigned-backed enum columns;
- direct/converter/enum unsigned keys for DeleteAll fixtures.

Seed scalar writes through Inquiry so stored bit patterns match production behavior.

Boundary values:

- `sbyte.MinValue` and `-1` → byte 128/255;
- `ushort` 40,000 and MaxValue → negative short partners;
- `uint` 3,000,000,000 and MaxValue → negative int partners;
- `ulong` above `long.MaxValue` and MaxValue → negative long partners;
- enum members using the same above-signed-maximum patterns.

For both providers assert:

1. positive IN returns exactly the boundary rows;
2. converter-backed IN returns the same rows and invokes conversion once per element;
3. unsigned-enum IN succeeds;
4. mixed/all-null nullable input behaves without provider type inference failure;
5. empty IN returns no rows;
6. NOT IN preserves expected complement/empty semantics;
7. direct, converter, and enum DeleteAll remove only requested keys;
8. empty/null DeleteAll remains a no-op;
9. SQL Server provisioned artifact validation stays green and uses the existing signed signatures;
10. PostgreSQL parameters are reported/inferred as signed array types, not unsupported unsigned CLR arrays.

## Documentation

Document that Inquiry stores unsigned/sbyte values through same-width provider-supported partners and applies the same rule to native collections. The reinterpretation is lossless at the bit level, including above-signed-maximum values and unsigned-backed enums.

Clarify that:

- PostgreSQL arrays are signed partner arrays;
- SQL Server TVPs reuse signed #199 artifacts;
- JSON dialect behavior is unchanged;
- nullable model elements become nullable signed storage and do not invoke converters;
- this issue does not add stored-procedure TVPs or redesign TVP streaming/metadata.

## Validation

```powershell
dotnet test tests/Inquiry.Generators.Tests/Inquiry.Generators.Tests.csproj -c Release --no-restore
dotnet test tests/Inquiry.Tests/Inquiry.Tests.csproj -c Release --no-restore

dotnet test tests/Inquiry.PostgreSql.Tests/Inquiry.PostgreSql.Tests.csproj -c Release -f net8.0 --no-restore
dotnet test tests/Inquiry.PostgreSql.Tests/Inquiry.PostgreSql.Tests.csproj -c Release -f net9.0 --no-restore
dotnet test tests/Inquiry.PostgreSql.Tests/Inquiry.PostgreSql.Tests.csproj -c Release -f net10.0 --no-restore

dotnet test tests/Inquiry.SqlServer.Tests/Inquiry.SqlServer.Tests.csproj -c Release -f net8.0 --no-restore
dotnet test tests/Inquiry.SqlServer.Tests/Inquiry.SqlServer.Tests.csproj -c Release -f net9.0 --no-restore
dotnet test tests/Inquiry.SqlServer.Tests/Inquiry.SqlServer.Tests.csproj -c Release -f net10.0 --no-restore

dotnet test tests/Inquiry.Sqlite.Tests/Inquiry.Sqlite.Tests.csproj -c Release --no-restore
dotnet test tests/Inquiry.MySql.Tests/Inquiry.MySql.Tests.csproj -c Release --no-restore
dotnet test tests/Inquiry.MariaDb.Tests/Inquiry.MariaDb.Tests.csproj -c Release --no-restore
dotnet test tests/Inquiry.Oracle.Tests/Inquiry.Oracle.Tests.csproj -c Release --no-restore

dotnet build Inquiry.slnx -c Release --no-restore
dotnet test Inquiry.slnx -c Release --no-build
dotnet publish tests/Inquiry.AotSmoke/Inquiry.AotSmoke.csproj -c Release
dotnet pack Inquiry.slnx -c Release --no-build
git diff --check
```

Final generated/runtime audit must find no `BindUnsupported` for the four unsigned/sbyte categories, no unsigned PostgreSQL array values, no duplicate converter calls, and no provider-specific projection changes in the four JSON dialects.
