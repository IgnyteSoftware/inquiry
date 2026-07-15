# MySQL-family P0 correctness implementation plan

**Issues:** #58, #169  
**Release gate:** #171  
**Target branch:** `prerelease`  
**PR shape:** one MySQL-family correctness PR with separately reviewable commits for #58 and #169.

## Outcomes

1. MySQL and MariaDB emit valid, type-correct `JSON_TABLE` SQL for every supported collection element.
2. JSON-array binding emits standards-compliant, culture-invariant JSON without reflection-heavy hot paths.
3. Enum-as-string and converter-backed collections use their effective provider representation consistently.
4. Unsupported converter provider types fail at build time through INQ038.
5. MySQL-family database-default GUID inserts never reference an unbound key parameter.
6. MariaDB exposes native single-row `DELETE ... RETURNING` through a generated, type-safe store method.
7. Generator, runtime, live-provider, NativeAOT, package, documentation, and benchmark gates pass before merge.

## Phase 1 — characterize failures with tests

Add failing tests before production changes:

- `InquiryJsonArrayParameterTests` for null/empty collections, embedded nulls, escaping, every numeric family,
  enums, char, GUID, date/time values, byte arrays, invariant culture, non-finite floating point, and a
  single-use enumerable.
- Generator assertions that MySQL/MariaDB use real `JSON_TABLE` column types and that enum-as-string uses
  text rather than its enum underlying integer.
- Converter generator tests for supported provider primitives and an unsupported provider type diagnostic.
- Generator assertions mechanically comparing SQL parameter tokens with the emitted binder for nullable
  database-default GUID insert/upsert paths.
- Delete-returning generator tests covering API shape, MariaDB SQL, concurrency binding, soft/hard delete,
  and unsupported-dialect diagnostics.

Baseline evidence:

- The reviewed CI run attributes 11 failures in each MySQL-family job to invalid `SIGNED PATH` SQL.
- Two MariaDB failures per TFM come from an undefined nullable GUID key parameter.
- Existing focused generator tests pass because they snapshot the invalid SQL.
- Local live reproduction is currently unavailable because Docker Desktop is not running.

## Phase 2 — make provider type truth consistent

Files:

- `src/Inquiry.Generators.Shared/EntityProcessor.cs`
- `src/Inquiry.Generators.Shared/Models/ConverterData.cs`
- `src/Inquiry.Generators.Shared/MaterializerEmitter.cs`
- `src/Inquiry.Generators.Shared/StoreOperationEmitter.cs`
- `src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs`

Changes:

1. `ColumnData.TypeClass` represents the effective stored value:
   - enum-as-string -> `DbTypeClass.String`;
   - converter -> converter provider `TypeData`;
   - otherwise -> model `TypeData`.
2. Extend `ConverterData` with symbol-free provider `TypeData`; use it consistently for DDL, parameter
   metadata/value coercion, materialization, and collection projection.
3. Assign reserved diagnostic INQ038 to unsupported converter provider primitives. Accept the explicitly
   supported scalar set; reject custom objects, collections, and ambiguous provider shapes before emission.
4. Update diagnostic registry documentation and generator tests. Coordinate release metadata with #135,
   without silently claiming #135 complete.

## Phase 3 — exhaustive AOT-safe JSON array binding

File: `src/Inquiry/Parameters/InquiryJsonArrayParameter.cs`

Implement a manual, generic serializer with dispatch selected once per closed `T`. Do not use reflection-based
`JsonSerializer.Serialize(object)` in the execution path.

Required representations:

- strings/chars: JSON strings with complete escaping;
- bool: JSON booleans;
- signed/unsigned integers and enums: invariant JSON numbers, preserving Inquiry's signed-storage
  reinterpretation contract;
- float/double/decimal: invariant JSON numbers; non-finite values fail explicitly;
- GUID: canonical JSON string;
- DateTime/DateTimeOffset/DateOnly/TimeOnly: deterministic formats proven against both engines;
- byte array: base64 JSON string, paired with a SQL-side decode expression;
- null element: JSON `null` rather than silent removal.

Semantics:

- null collection -> `[]`;
- empty collection -> `[]`;
- `IN` containing nulls only matches no rows under SQL three-valued logic;
- `NOT IN` retains the existing sentinel-expansion path.

## Phase 4 — consolidate and correct MySQL-family JSON_TABLE SQL

Files:

- `src/Inquiry.Generators.Shared/Abstractions/MySqlFamilySqlBuilder.cs`
- `src/Inquiry.MySql.Analyzer/MySqlSqlBuilder.cs`
- `src/Inquiry.MariaDb.Analyzer/MariaDbSqlBuilder.cs`

Move `UseArrayInParameters`, `ArrayParameterBinderFqn`, and `RenderIn` into the family builder. Remove the
duplicated concrete implementations.

The family mapping owns both the `JSON_TABLE` target type and any extraction transform so they cannot drift:

- integral/bool -> valid matching integer types;
- float/double/decimal -> matching numeric types;
- string/enum-as-string -> non-truncating text extraction;
- GUID -> `CHAR(36)`;
- date/time -> matching temporal types;
- binary -> text extraction plus `FROM_BASE64`.

No generated SQL may contain `COLUMNS(val SIGNED ... )`. Long strings and binary data must not be silently
truncated. Live tests, not syntax assumptions, decide the final engine-compatible declarations.

## Phase 5 — GUID insert-returning SQL/binder lockstep

Files:

- `src/Inquiry.Generators.Shared/Abstractions/MySqlFamilySqlBuilder.cs`
- `src/Inquiry.MariaDb.Analyzer/MariaDbSqlBuilder.cs`
- relevant generator and provider GUID tests

Rules:

- Ordinary insert-returning deliberately excludes a `UseDatabaseDefault` key from its binder. Its SQL must
  therefore generate/use the server value without referencing `@Id`.
- MariaDB insert-returning should allow the column default to generate the GUID and return it.
- MySQL emulation may assign a session variable from `UUID()`, but must not reference an unbound key.
- Explicit-key upsert remains a distinct path with `includeKey: true` and may reference the key parameter.
- MySQL and MariaDB snapshots assert both SQL and binder contents.

## Phase 6 — MariaDB DELETE RETURNING

Files:

- `src/Inquiry/Stores/InquiryDeleteOneByKeyAttribute.cs`
- `src/Inquiry.Generators.Shared/StoreProcessor.cs`
- `src/Inquiry.Generators.Shared/StoreOperationEmitter.cs`
- `src/Inquiry.Generators.Shared/Abstractions/SqlBuilder.cs`
- `src/Inquiry.MariaDb.Analyzer/MariaDbSqlBuilder.cs`
- generator/live tests and MariaDB documentation

API:

```csharp
[InquiryDeleteOneByKey(ReturnEntity = true)]
partial Task<TEntity?> DeleteReturningAsync(TKey key, CancellationToken cancellationToken = default);
```

Behavior:

- `ReturnEntity = false` preserves `Task<bool>`.
- `ReturnEntity = true` requires `Task<TEntity?>`.
- MariaDB hard/ordinary delete emits native single-table `DELETE ... RETURNING`.
- Missing row returns null.
- Concurrency-token methods bind key + token and preserve configured conflict behavior.
- Soft-delete returning has an explicit, tested contract; do not silently use physical delete.
- Unsupported dialects produce the existing compile-time unsupported-operation diagnostic and no invalid SQL.

Open a separate follow-up for PostgreSQL, SQLite, SQL Server, Oracle, and any safe MySQL emulation. This PR
establishes the public seam and MariaDB implementation; it does not claim cross-provider parity.

## Phase 7 — live coverage and benchmarks

Live MySQL and MariaDB coverage on net8/net9 (and scheduled net10):

- predicate IN, enum-as-string IN, predicate mutation IN, and batch delete;
- cardinalities 0, 1, 4, 16, 100, 1,000 with constant SQL text;
- representative integer, string, enum, converter, temporal, GUID, and binary round trips;
- nullable/default GUID insert-returning and explicit/null-key upsert;
- MariaDB delete-returning existing/missing/concurrency/soft/hard cases;
- `AllowUserVariables=false` for MariaDB returning paths.

Benchmarks:

- JSON writer versus the prior serializer at 0/1/8/64/1024 integers, escaped strings, dates, binary,
  and enums;
- live JSON_TABLE versus scalar expansion at required cardinalities on both engines;
- implement a small-list branch only if measurements justify the additional SQL/plan-cache shape.

Store reproducible BenchmarkDotNet artifacts and decision notes. Merely adding benchmark code does not satisfy
#169.

## Adversarial review checklist

- Compare every generated SQL parameter token against parameters created by its binder.
- Verify enum/converter type agreement across DDL, parameter, JSON_TABLE, materializer, and bulk metadata.
- Exercise quotes, controls, Unicode, unpaired surrogates, strings over 255 characters, numeric boundaries,
  NaN/infinity, offsets/kinds, midnight/max time, empty/repeated binary, duplicate/null elements, and
  single-use enumerables.
- Confirm MySQL snapshots/behavior do not accidentally inherit MariaDB-native returning.
- Confirm null/missing delete rows and concurrency conflict behavior.
- Inspect NativeAOT/trim warnings and benchmark allocations.
- Run an independent adversarial code review before publishing the PR.

## Required validation before PR and before merge

1. `dotnet test` for generator and runtime projects on net8/net9/net10.
2. Full MySQL and MariaDB live projects on net8/net9 with `INQUIRY_REQUIRE_DOCKER=1`; no skips.
3. Scheduled-equivalent net10 provider tests.
4. Full solution Release build and non-Docker test suite.
5. NativeAOT publish/smoke test.
6. Pack all nine packages and inspect analyzer/package contents.
7. DocFX build and `git diff --check`.
8. Adversarial review clean or all findings resolved.
9. PR targets `prerelease`; wait for Copilot review, resolve feedback, rerun relevant and full local tests,
   then merge.

## Explicitly separate work

- Cross-provider DELETE RETURNING parity.
- DateTimeOffset storage/offset preservation beyond deterministic JSON binding (#174).
- General provider type-system refactoring beyond the effective stored-type correction (#174/#184).
- `NOT IN` array binding; the existing expansion behavior remains.
- Unrelated #171 failures: incomplete Product fixtures, timestamp precision assertions, pipeline cancellation,
  and MySQL net8 generated-default-returning isolation beyond the reproduced GUID path.
