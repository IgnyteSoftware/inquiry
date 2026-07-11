# Issue #173: complete provider parameter metadata

## Goal

Ensure every generated column-backed scalar binder emits provider-aware `DbType` metadata while preserving SQL Server predicate size/precision signatures and never adding truncating size metadata to write values.

## Confirmed defects

Three scalar paths in `StoreOperationEmitter` emit name/value and optional predicate size metadata without `DbType`:

1. predicate select and exists;
2. update/delete predicate WHERE parameters;
3. offset-paged field filters.

The shared mapper also lacks portable `byte[]` and `DateTimeOffset` mappings, and converter columns reduce provider types to `SpecialType`, losing Guid, temporal, and binary metadata plus Oracle's DateTime override.

## Implementation

1. Add generator regressions anchored to each broken method/parameter block across SQL Server, PostgreSQL, SQLite, MySQL, MariaDB, and Oracle.
2. Introduce one source-emission helper for column parameter metadata. It emits provider-aware `DbType` and conditionally emits size/precision for predicate parameters.
3. Route all generated column-backed scalar parameter blocks through the helper, including already-correct paths, without changing names, order, value expressions, allocations, or command shape.
4. Keep writes and mutation SET values free of `.Size`/precision narrowing metadata; predicates, keys, and cursors retain stable signature metadata.
5. Map `byte[]` to `DbType.Binary` and `DateTimeOffset` to `DbType.DateTimeOffset`.
6. Resolve converter metadata from `ConverterData.ProviderType`; retain the built-in JSON string fallback. Oracle converter-backed DateTime must remain `DbType.DateTime`.
7. Add a SQL Server live `VARCHAR(64)` predicate fixture with a nonclustered index. Assert the cached parameter signature is `varchar(64)`, the plan uses a seek predicate, and no `CONVERT_IMPLICIT` is applied to the indexed column.

## Tests

- Each of predicate select/exists, mutation WHERE, and offset-paged field filters emits the correct metadata.
- ANSI/Unicode strings, decimals, enums, enum-as-string, nullable scalars, dates, DateTimeOffset, Guid, binary, and converters are covered across six dialects.
- SQL Server alone emits predicate size/precision; mutation SET/write values remain non-narrowing.
- Converter provider Guid, binary, DateTimeOffset, and DateTime use provider types, including Oracle's DateTime override.
- SQL Server live plan evidence proves stable varchar signatures and seek-compatible behavior.

## Stored-procedure boundary

Stored-procedure inputs are out of scope for #173. They have no entity-column metadata from which to derive Unicode, length, precision, or scale. A separate issue must define explicit procedure-parameter metadata rather than guessing defaults in this P0 correctness fix.

## Validation gates

- Full generator suite on .NET 8, 9, and 10.
- SQL Server live parameter-signature and execution-plan tests on supported TFMs.
- Release solution build, relevant provider tests, package and documentation checks.
- `git diff --check`.
- Independent adversarial review before PR publication.
