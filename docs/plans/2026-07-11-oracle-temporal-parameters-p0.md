# Issue #190: Oracle temporal parameter metadata

## Proven provider contract

ODP.NET Core 23.26.200 against Oracle XE 21 established:

- `DateOnly` with `DbType.Date` fails; midnight `DateTime` with `DbType.Date` succeeds.
- `TimeOnly` with `DbType.Time` fails.
- `TimeSpan` with `DbType.Time` or `DbType.Object` fails.
- `TimeSpan` with no `DbType` infers `OracleDbType.IntervalDS` and succeeds.
- `DateTimeOffset` with `DbType.DateTimeOffset` succeeds as `TimeStampTZ`.

Required Oracle generated values:

- DateOnly: `value.ToDateTime(TimeOnly.MinValue)` plus `DbType.Date`.
- TimeOnly: `value.ToTimeSpan()` with no `DbType` assignment.
- DateTimeOffset: unchanged value plus `DbType.DateTimeOffset`.

## Architecture

1. Make DateOnly, TimeOnly, and DateTimeOffset `DbType` expressions provider-overridable in `SqlBuilder`; preserve existing defaults for other providers. Oracle returns `null` for TimeOnly metadata.
2. Add a compile-time parameter-value expression hook carrying the effective provider type. The base hook is identity; Oracle bridges DateOnly and TimeOnly.
3. Refactor column value emission so ordering is: nullable guard → converter `ToProvider` → provider bridge → unsigned storage reinterpretation → object/DBNull.
4. Apply the shared value hook to all column-backed scalar paths: CRUD/returning, predicates, mutations, paging/cursors, batch insert/update, bulk accessor definitions, eager/relation key parameters.
5. Leave stored-procedure inputs unchanged because they lack `ColumnData` and are tracked by #188.
6. Do not rewrite JSON/native-array collection elements; their invariant textual wire protocol is separate from scalar binding.

## Tests

- Oracle generator snapshots for direct and nullable DateOnly/TimeOnly/DateTimeOffset.
- Converter-provider DateOnly/TimeOnly/DateTimeOffset ordering and null guards.
- Insert/update/upsert/returning, predicate/mutation, InsertAll/UpdateAll, cursor/eager/relation paths use identical transformations.
- Other dialects remain byte-for-byte equivalent.
- Collection projections contain no scalar temporal bridge.
- Oracle live insert-returning, update-returning, scalar predicates, nullable values, converter round trips, and batch writes.
- DateOnly leap/boundary dates preserve midnight semantics.
- TimeOnly preserves seven fractional-second digits.
- DateTimeOffset preserves both UTC instant and a non-hour numeric offset.
- Direct ODP characterization pins known-good and known-bad metadata/value combinations.

Run Oracle live tests on .NET 8, 9, and 10 plus the full generator/runtime/release/package/documentation gates. Independent adversarial review is required before publication.
