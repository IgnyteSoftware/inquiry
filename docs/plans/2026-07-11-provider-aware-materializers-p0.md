# Issue #174: provider-aware reader expressions

## Goal

Generate the fastest correct reader expression for each logical type and provider without adding reflection, `dynamic`, `GetValue`, or blanket conversion to generated hot paths. Grouped and scalar counts must be correct on all six providers and defined beyond `Int32`.

## Architecture

1. Create the elected dialect's `SqlBuilder` before entity, projection, ad-hoc, store, and schema emission.
2. Introduce a compile-time reader-expression context describing ordinal, logical/provider type, and result role. `SqlBuilder` exposes a reader-expression hook whose base implementation preserves current direct typed getters.
3. Thread the builder through `EntityProcessor`, `ProjectionProcessor`, `AdHocProcessor`, and `MaterializerEmitter`.
4. Keep null handling, converter `FromProvider`, enum wrapping, and unsigned bit reinterpretation centralized in `MaterializerEmitter`; only the provider primitive read is delegated.
5. Remove GroupCount's bespoke `GetFieldValue<TKey>`/`GetInt64` path. Resolve its `ColumnData` and route both the grouped key and synthetic `Int64` count through the same reader hook.
6. Make the count SQL function provider-selectable. SQL Server emits `COUNT_BIG(*)` for scalar and grouped count; the base remains `COUNT(*)`.

## Oracle reader policy

Characterize live ODP.NET representations before pinning snapshots. Expected generated reads:

- signed integer widths: checked casts from `GetDecimal`;
- decimal: direct decimal getter;
- float/double: provider-supported typed getters;
- `NUMBER(1)` Boolean: `GetDecimal(...) != 0m`;
- count: checked `Int64` from decimal if live characterization requires it;
- RAW Guid: prefer `GetGuid`; only use `new Guid(byte[])` if a live byte-order round trip proves necessary;
- binary: `GetFieldValue<byte[]>`;
- DateTime: `GetDateTime`;
- DateTimeOffset: `GetFieldValue<DateTimeOffset>`;
- DateOnly: `DateOnly.FromDateTime(GetDateTime(...))`;
- TimeOnly: `TimeOnly.FromTimeSpan(GetFieldValue<TimeSpan>(...))`.

Converter provider types use the same hook. Generated INSERT/UPDATE/UPSERT returning paths already reuse entity materializers and must remain aligned. Oracle numeric narrowing and counts are checked; existing unsigned and enum storage reinterpretation remains intentionally unchecked.

## Red-green order

1. Add generator regressions for SQL Server `COUNT_BIG`, provider-aware GroupCount keys/counts, and direct-getter base behavior.
2. Add focused entity/projection/ad-hoc/converter materializer snapshots proving the hook reaches every funnel and generated sources contain no `GetValue`, reflection, or `Convert.ChangeType`.
3. Move builder creation earlier and thread it through all materializer processors.
4. Add the shared reader hook and preserve base direct getter output byte-for-byte where representations match.
5. Unify GroupCount materialization and add SQL Server count-function override.
6. Add Oracle live characterization and then its minimal typed-reader override.
7. Add live GroupCount coverage on SQLite, PostgreSQL, SQL Server, MySQL, MariaDB, and Oracle; include empty/small results and SQL Server overflow-safe SQL evidence.
8. Add Oracle live ordinary-select and generated-return coverage for NUMBER widths, decimal, Boolean, Guid/RAW, binary, DateTime, DateTimeOffset, DateOnly, TimeOnly, converters, and generated identity values.

## Performance invariants

- Direct typed getters remain the default.
- No runtime provider branches, delegates, reflection, boxing conversion helpers, or blanket `Convert.ChangeType` in row materializers.
- Provider differences are resolved entirely at generation time.
- Null, enum, converter, and unsigned composition stays centralized to avoid duplicated branches.
- SQL Server obtains 64-bit count semantics in SQL, not through per-row conversion.

## Validation gates

- Full generator suite on .NET 8, 9, and 10.
- GroupCount live tests on all six providers and supported TFMs.
- Focused Oracle materialization/returning matrix on all supported TFMs.
- Runtime tests, Release solution build, nine-package pack, DocFX, and `git diff --check`.
- Independent adversarial review before PR publication.
