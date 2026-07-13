# Issue #134: all-types bulk-insert matrix

## Scope

Complete the correctness contract for the existing bulk-insert path without absorbing the typed-accessor,
allocation, options, telemetry, or benchmark work owned by #183. The current `prerelease` baseline is green
for SQLite, PostgreSQL, MySQL, MariaDB, and Oracle; SQL Server fails when `SqlBulkCopy` requests a stream for
the binary column and `InquiryBulkRowReader.GetBytes` throws.

## Implementation

1. Extend `InquiryBulkInsertDefinition` with generated `FieldTypes` metadata describing the exact CLR value
   returned by its post-converter/post-enum/post-provider-bridge accessor. Keep `ColumnTypes` for provider wire
   metadata, validate both optional arrays against `Columns.Count`, and update the documentation to reflect
   that SQL Server/MySQL readers also consume the field shape.
2. Complete the `DbDataReader` contract used by bulk-copy providers:
   - return generated `FieldTypes` before `Read`, on `DBNull`, across later rows, and after EOF; preserve the
     value-based fallback only for manually constructed definitions without field metadata;
   - implement `GetBytes` for generated `byte[]` values and `GetChars` for generated `string` values. A null
     destination is a full-field length probe regardless of non-negative source offset. Non-null calls copy
     only the available data, return zero at/past EOF without narrowing overflowing `long` offsets, do not
     mutate the destination for zero-length reads, and reject negative offsets/counts or impossible buffer
     ranges before copying. Invalid ordinals throw `IndexOutOfRangeException`; wrong types and `DBNull` throw
     `InvalidCastException`;
   - implement a one-row lookahead so `HasRows` is correct before `Read` for empty/non-empty streams without
     increasing `RowsRead` until consumption or buffering more than one entity; track `IsClosed` after disposal.
3. Add focused reader unit tests for metadata validation; field-type stability at every reader state;
   binary/string length probes and partial copies; nonzero offsets; empty values; offset equal to, greater than,
   and vastly greater than field length; zero-length reads; invalid ordinals/types/nulls/ranges; lookahead row
   accounting; empty/non-empty `HasRows`; and disposed state.
4. Replace the one-row all-types fixture with a shared provider-neutral case set that covers:
   - empty input, single-row and multi-row calls;
   - nullable string null/non-null;
   - portable minimum and non-empty string/binary values (reader unit tests cover empty values);
   - both booleans and first/last enum values;
   - converter-backed zero/positive/negative values;
   - exact portable boundary rows, not representative examples: `int.MinValue`/zero/`int.MaxValue`;
     `-999999999999.25`, zero, and `999999999999.25` for decimal and converter values (the integer
     component stays below 2^53 and the quarter fraction is binary-exact, so SQLite `NUMERIC` affinity can
     round-trip the same shared values exactly);
     `Guid.Empty`, all-FF, and a deterministic middle value; ASCII one-character/middle/exact-200-character
     strings (Oracle stores empty `VARCHAR2` as null, so no cross-provider exact empty-string value exists);
     single-byte/4-byte/4 KiB binary values (Oracle likewise binds an empty BLOB as null); and common
     provider-safe microsecond timestamps from
     `1000-01-01 00:00:00` through `9999-12-31 23:59:59.999999`.
   Each provider test must assert the returned count for empty, single, and multi-row calls, zero persisted rows
   after the empty call, exact final row count, and field-by-field equality for every inserted row.
5. Run the same case set through SQLite and all five server providers on .NET 8, 9, and 10. Provider tests
   may normalize only documented catalog/value representations; they must not weaken value equality.

## Non-goals

- typed or generic value accessors and boxing removal (#183)
- bulk-copy transaction/options/telemetry redesign (#183)
- SQL Server TVP lifecycle or metadata (#69)
- full provider-suite restoration and two-green-run evidence (#171/#89)

## Acceptance evidence

- `InquiryBulkRowReaderTests` pass on .NET 8/9/10.
- `BulkAllTypesIntegrationTests` pass on all six providers on .NET 8/9/10 with Docker required for servers.
- Full generator, core, and SQLite suites remain green.
- Release solution build, package smoke, pack, and DocFX remain green in proportion to the changed surface.
- An adversarial review finds no contract weakening, collection materialization or buffering beyond the native
  reader's single lookahead entity, provider-specific bypass, or scope leakage into #183. Existing fallback
  batching behavior is outside this no-new-buffering assertion.
