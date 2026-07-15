# Oracle Guid and Boolean parameter binding (#192)

## Goal

Make every column-backed Oracle binder select the ODP.NET-compatible metadata for `Guid`/`RAW(16)` and `bool`/`NUMBER(1)` without changing the CLR value expression or adding hot-path work. This is a P0 correctness prerequisite for the live-provider release gate (#171).

## Characterized ODP.NET contract

Live isolation used Oracle.ManagedDataAccess.Core 23.26.200 against Oracle XE 21c.

- A CLR `Guid` succeeds against `RAW(16)` when `DbType.Binary` is assigned **before** `Value`.
- The value stays a CLR `Guid`; no `Guid.ToByteArray()` conversion is needed.
- `00112233-4455-6677-8899-aabbccddeeff` stores as `33221100554477668899AABBCCDDEEFF`, and `OracleDataReader.GetGuid` returns the original value.
- Assigning a Guid without selecting binary metadata fails at the `Value` setter. `DbType.Guid` is rejected.
- A CLR `bool` with `DbType.Int32` succeeds against `NUMBER(1)` and stores `false`/`true` as `0`/`1`.
- Bool + Int32 works in either metadata/value assignment order. Omitted metadata and `DbType.Boolean` fail with ORA-00932.

The implementation is therefore metadata-only:

```csharp
parameter.DbType = DbType.Binary;
parameter.Value = (object)entity.GuidValue;

parameter.DbType = DbType.Int32;
parameter.Value = (object)entity.BoolValue;
```

Do not emit `ToByteArray()`, a Boolean `? 1 : 0` conversion, provider runtime checks, or new value-hook context flags.

## Production design

### Provider-overridable metadata

In `src/Inquiry.Generators.Shared/Abstractions/SqlBuilder.cs`, add portable defaults:

```csharp
public virtual string? GuidDbTypeExpression
    => "global::System.Data.DbType.Guid";

public virtual string? BooleanDbTypeExpression
    => "global::System.Data.DbType.Boolean";
```

Route `MapDbTypeExpression(TypeData, ...)` through these properties before `DbTypeMapper`:

1. Guid
2. Boolean
3. existing DateTime/DateOnly/TimeOnly/DateTimeOffset routing
4. portable fallback

Route Boolean through `BooleanDbTypeExpression` in `MapDbTypeExpressionForSpecialType` as well. This preserves the fallback used by converter metadata that has only a `SpecialType`. Guid converter providers carry full `ProviderType` metadata and use `MapDbTypeExpression`.

In `src/Inquiry.Oracle.Analyzer/OracleSqlBuilder.cs`, override only the metadata:

```csharp
public override string? GuidDbTypeExpression
    => "global::System.Data.DbType.Binary";

public override string? BooleanDbTypeExpression
    => "global::System.Data.DbType.Int32";
```

Do not modify `BuildParameterValueExpression` or `ParameterValueExpressionContext`. Non-Oracle builders inherit byte-for-byte-equivalent Guid and Boolean behavior.

### Existing binder funnel and ordering

`ResolveDbType` already supplies column/provider metadata to all required paths. Verify rather than duplicate it for:

- insert, update, upsert, and returning variants;
- select/delete/restore keys and concurrency tokens;
- field, scalar predicate, predicate-mutation, and keyset values;
- `InsertAll` and `UpdateAll`;
- eager/grid/relation inline `InquiryParameter` values;
- converter-backed and nullable columns.

The Guid ordering requirement is already satisfied in each runtime shape:

- generated binder lambdas call `AppendColumnParameterMetadata` before setting `Value`;
- `InquiryParameterBinder` assigns `DbType` before `Value`;
- `InquiryInExpansion` assigns element `DbType` before element `Value`;
- Oracle batch execution uses the sequential generated scalar binder.

Add regression assertions for that ordering. Do not rely on source inspection alone.

### Connection finalizer

Keep `OracleInquiryConnectionFactory.FinalizeCommand`'s Boolean normalization. Generated code will arrive with `DbType.Int32` and bypass that branch, but it remains a defensive compatibility path for hand-authored/ad-hoc `InquiryParameter` values using `DbType.Boolean`.

Do not add a Guid conversion to the finalizer: ODP.NET rejects a Guid before finalization when binary metadata was not selected first.

## Separate follow-up: reject nullable converter provider types (#194)

Nullable converter provider types are explicitly out of scope for #192 and are tracked separately by #194. They are not a prerequisite for the metadata-only Oracle fix because supported converters with non-null `Guid`/`bool` provider types already flow through `ResolveDbType` correctly.

Current source evidence proves the unsafe state:

- `TypeData.Create` records provider nullability but classifies `Guid?`, `bool?`, and annotated nullable reference providers by their non-null underlying type.
- `IsSupportedConverterProviderType` does not reject `type.IsNullable`, so those providers are accepted.
- The generated converter binder guards model nullability, not provider-result nullability.
- A non-null model whose `ToProvider` returns null can emit `(object)providerValue`, producing `null` rather than `DBNull.Value` and violating the binder contract.
- The DDL/materializer null contract is controlled by the model property, so nullable `TProvider` would introduce a second, currently undefined nullability contract.

Issue #194 should use existing error INQ038 to reject nullable `TProvider` types. Require `!type.IsNullable` in `IsSupportedConverterProviderType`; after reporting INQ038, suppress use of the invalid converter for emission so the user receives the intended diagnostic rather than cascading generated-code errors.

Test `Guid?`, `bool?`, and `string?` provider types. Also prove that a nullable model property with a non-null Guid/bool provider remains supported and guards `ToProvider` correctly. Do not attempt to define nullable-provider semantics in #192.

## Collection and stored-procedure scope

### Collections

Keep Oracle `IN` as a single JSON-backed parameter, but make its server-side projection match scalar storage:

- Guid values remain canonical JSON strings; SQL reorders the first 4/2/2-byte fields before `HEXTORAW` so they compare equal to ODP.NET's mixed-endian `RAW(16)` representation.
- Boolean values remain JSON `true`/`false`; `JSON_TABLE` projects text and a `CASE` maps it to `1`/`0`. This stays compatible with the advertised Oracle 12c+ range and avoids relying on the newer `ALLOW BOOLEAN TO NUMBER CONVERSION` clause.
- Converter collections still apply `ToProvider` before serialization.

Oracle `NOT IN` uses per-element expansion. It automatically receives `DbType.Binary`/`DbType.Int32` through `ResolveDbType`, with metadata set before each raw CLR Guid/bool value. Cover both `IN` and `NOT IN` because they use different runtime paths.

### Stored procedures

Stored-procedure parameters are not column-backed and do not use `ResolveDbType`. Do not expand #192 into a partial stored-procedure type system. Provider-aware stored-procedure input/output metadata and value handling is already tracked by #188.

## Exact files

Production:

- `src/Inquiry.Generators.Shared/Abstractions/SqlBuilder.cs`
- `src/Inquiry.Oracle.Analyzer/OracleSqlBuilder.cs`
- `docs/site/articles/providers/oracle.md`

Tests:

- add `tests/Inquiry.Generators.Tests/OracleGuidBooleanParameterGeneratorTests.cs`
- extend `tests/Inquiry.Generators.Tests/ConverterGeneratorTests.cs`
- extend `tests/Inquiry.Tests/InquiryParameterBinderTests.cs`
- extend `tests/Inquiry.Oracle.Tests/OracleReaderRepresentationTests.cs`
- extend `tests/Inquiry.Oracle.Tests/ProviderAwareMaterializerIntegrationTests.cs`

## Red generator tests

Generate an Oracle entity/store with direct, nullable, converter-backed, key, and relation-key Guid/bool columns. Cover insert/returning, update/returning, upsert where supported, key/field predicates, predicate mutations, keyset, `InsertAll`, `UpdateAll`, eager grid/separate relations, `IN`, and `NOT IN`.

Assert:

- Oracle Guid metadata is `DbType.Binary`.
- Oracle Boolean metadata is `DbType.Int32`.
- metadata statements precede value statements;
- values remain raw CLR Guid/bool expressions after any converter `ToProvider` call;
- nullable models guard converter invocation and map model null to `DBNull`;
- no `ToByteArray()` or `? 1 : 0` appears;
- no new provider-value hook flags appear;
- SQLite, SQL Server, PostgreSQL, MySQL, and MariaDB retain `DbType.Guid`/`DbType.Boolean` and their current value expressions;
- Oracle JSON `IN` remains one string parameter and projects Guid/bool values into their scalar storage representations;
- Oracle expanded `NOT IN` receives Binary/Int32 metadata.

Nullable converter provider diagnostics and emission suppression belong to #194. In #192, prove only that nullable model properties with non-null Guid/bool provider types remain valid.

## Direct ODP characterization tests

In `OracleReaderRepresentationTests`, permanently pin rejected and accepted pairs.

Guid:

1. Guid without metadata fails at `Value` assignment.
2. `DbType.Guid` is rejected.
3. Set `DbType.Binary`, then a CLR Guid; execute successfully.
4. Assert exact `RAWTOHEX` byte order.
5. Assert `GetGuid` returns the original value.

Boolean:

1. Omitted metadata and `DbType.Boolean` fail against `NUMBER(1)`.
2. CLR false/true + `DbType.Int32` succeeds.
3. Exercise both metadata-before-value and value-before-metadata.
4. Query and assert exact numeric `0`/`1`.

## Live generated coverage

Extend the Oracle all-types fixture so Inquiry, not raw SQL, performs:

- insert-returning and update-returning;
- direct and nullable Guid/bool round trips;
- scalar Guid/bool predicates;
- converter-backed Guid/bool round trips and predicates;
- `InsertAll` and `UpdateAll`;
- Guid/bool `IN` and `NOT IN` including converter collections where the generated path is supported;
- a Guid key eager/relation lookup only when it can reuse the existing fixture without distorting the model.

Assert false and true store as NUMBER(1), Guid byte order and `GetGuid` round-trip remain exact, and nulls remain database NULL. The focused local live gate runs on net8.0; the multi-target CI matrix exercises the same test on net9.0 and net10.0.

## Performance invariants

- No Guid byte-array allocation or copying.
- No Boolean conversion branch in generated binders.
- No reflection, `dynamic`, `Convert.ChangeType`, provider runtime type test, or new closure.
- Preserve existing raw value expressions and boxing count.
- Do not add eager Guid caches: metadata-only binding makes byte-array reuse unnecessary, and special caching would not eliminate the existing general value-type boxing contract.
- Preserve the converter-cache and JSON collection single-parameter/allocation profile.

## Documentation

Update the Oracle provider page to state:

- Guid columns use `RAW(16)` and generated binders select `DbType.Binary` before assigning the CLR Guid.
- The value is not converted to `byte[]`; ODP.NET preserves .NET Guid byte ordering and `GetGuid` reverses it.
- Boolean columns use `NUMBER(1)` and generated binders pair the CLR bool with `DbType.Int32`, producing `0`/`1`.
- finalizer Boolean normalization is only a compatibility fallback.
- temporal behavior from #193 remains unchanged.

## Validation

```powershell
dotnet test tests/Inquiry.Generators.Tests/Inquiry.Generators.Tests.csproj -c Release --no-restore

dotnet test tests/Inquiry.Oracle.Tests/Inquiry.Oracle.Tests.csproj -c Release -f net8.0 --no-restore
dotnet test tests/Inquiry.Oracle.Tests/Inquiry.Oracle.Tests.csproj -c Release -f net9.0 --no-restore
dotnet test tests/Inquiry.Oracle.Tests/Inquiry.Oracle.Tests.csproj -c Release -f net10.0 --no-restore

dotnet test tests/Inquiry.Sqlite.Tests/Inquiry.Sqlite.Tests.csproj -c Release --no-restore
dotnet test tests/Inquiry.SqlServer.Tests/Inquiry.SqlServer.Tests.csproj -c Release --no-restore
dotnet test tests/Inquiry.PostgreSql.Tests/Inquiry.PostgreSql.Tests.csproj -c Release --no-restore
dotnet test tests/Inquiry.MySql.Tests/Inquiry.MySql.Tests.csproj -c Release --no-restore
dotnet test tests/Inquiry.MariaDb.Tests/Inquiry.MariaDb.Tests.csproj -c Release --no-restore

dotnet build Inquiry.slnx -c Release --no-restore
dotnet test Inquiry.slnx -c Release --no-build
dotnet publish tests/Inquiry.AotSmoke/Inquiry.AotSmoke.csproj -c Release
git diff --check
```

Audit generated Oracle sources for forbidden value conversions and wrong metadata, while confirming non-Oracle sources retain their portable mappings.
